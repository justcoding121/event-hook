using System;
using System.Runtime.InteropServices;
using System.Threading;
using EventHook.Hooks;

namespace EventHook.Platforms.Linux
{
    /// <summary>
    /// XRecord-based global keyboard and mouse capture when <c>DISPLAY</c> is set.
    /// </summary>
    internal sealed class LinuxKeyboardMouseX11 : IDisposable
    {
        private readonly Action<LinuxKeySnapshot> onKey;
        private readonly Action<MouseSnapshot> onMouse;
        private Thread thread;
        private IntPtr controlDisplay;
        private IntPtr dataDisplay;
        private IntPtr context;
        private LinuxX11Native.XRecordInterceptProc interceptProc;
        private volatile bool running;
        private bool disposed;
        private Exception startError;
        private readonly ManualResetEventSlim ready = new ManualResetEventSlim(false);
        private ulong[] keysymsByKeycode = Array.Empty<ulong>();

        internal LinuxKeyboardMouseX11(Action<LinuxKeySnapshot> onKey, Action<MouseSnapshot> onMouse)
        {
            this.onKey = onKey;
            this.onMouse = onMouse;
        }

        internal HookStartResult Start()
        {
            if (!LinuxSession.HasX11Display)
            {
                return LinuxSession.RequireX11("Keyboard/Mouse");
            }

            if (running)
            {
                return HookStartResult.Ok();
            }

            LinuxSession.EnsureXInitThreads();

            running = true;
            interceptProc = OnIntercept;
            thread = new Thread(ThreadMain)
            {
                IsBackground = true,
                Name = "EventHook.Linux.XRecord"
            };
            thread.Start();

            if (!ready.Wait(TimeSpan.FromSeconds(10)))
            {
                Stop();
                return HookStartResult.Fail(HookFailureReason.NativeFailure, "Timed out starting XRecord.");
            }

            if (startError != null)
            {
                var message = startError.Message;
                Stop();
                return HookStartResult.Fail(HookFailureReason.NativeFailure, message);
            }

            return HookStartResult.Ok();
        }

        internal void Stop()
        {
            running = false;
            try
            {
                if (controlDisplay != IntPtr.Zero && context != IntPtr.Zero)
                {
                    LinuxX11Native.XRecordDisableContext(controlDisplay, context);
                    LinuxX11Native.XFlush(controlDisplay);
                }
            }
            catch
            {
                // ignore
            }

            if (thread != null && thread.IsAlive && Thread.CurrentThread != thread)
            {
                thread.Join(TimeSpan.FromSeconds(3));
            }

            thread = null;
        }

        private void ThreadMain()
        {
            try
            {
                controlDisplay = LinuxX11Native.XOpenDisplay(null);
                dataDisplay = LinuxX11Native.XOpenDisplay(null);
                if (controlDisplay == IntPtr.Zero || dataDisplay == IntPtr.Zero)
                {
                    startError = new InvalidOperationException("XOpenDisplay failed for XRecord.");
                    return;
                }

                if (LinuxX11Native.XRecordQueryVersion(controlDisplay, out _, out _) == 0)
                {
                    startError = new InvalidOperationException("XRecord extension is not available on this display.");
                    return;
                }

                LinuxX11Native.XSynchronize(dataDisplay, 1);
                CacheKeysyms(controlDisplay);

                var rangePtr = LinuxX11Native.XRecordAllocRange();
                if (rangePtr == IntPtr.Zero)
                {
                    startError = new InvalidOperationException("XRecordAllocRange failed.");
                    return;
                }

                var range = Marshal.PtrToStructure<LinuxX11Native.XRecordRange>(rangePtr);
                range.deviceEvents.first = LinuxX11Native.KeyPressDetail;
                range.deviceEvents.last = LinuxX11Native.MotionNotifyDetail;
                Marshal.StructureToPtr(range, rangePtr, false);

                var clients = new[] { (IntPtr)3 }; // XRecordAllClients
                var ranges = new[] { rangePtr };
                context = LinuxX11Native.XRecordCreateContext(
                    controlDisplay,
                    0,
                    clients,
                    1,
                    ranges,
                    1);

                LinuxX11Native.XFree(rangePtr);

                if (context == IntPtr.Zero)
                {
                    startError = new InvalidOperationException("XRecordCreateContext failed.");
                    return;
                }

                LinuxX11Native.XSync(controlDisplay, 0);
            }
            catch (DllNotFoundException ex)
            {
                startError = ex;
            }
            catch (Exception ex)
            {
                startError = ex;
            }

            if (startError != null || context == IntPtr.Zero || dataDisplay == IntPtr.Zero)
            {
                ready.Set();
                CleanupDisplays();
                return;
            }

            try
            {
                // Blocks until XRecordDisableContext from the control connection.
                // Start() waits for XRecordStartOfData so EnableContext is actually live.
                var status = LinuxX11Native.XRecordEnableContext(
                    dataDisplay, context, interceptProc, IntPtr.Zero);
                if (status == 0 && running)
                {
                    startError = new InvalidOperationException("XRecordEnableContext failed.");
                }
            }
            catch (Exception)
            {
                // shutdown path
            }
            finally
            {
                ready.Set();
                CleanupDisplays();
            }
        }

        private void CleanupDisplays()
        {
            try
            {
                if (controlDisplay != IntPtr.Zero && context != IntPtr.Zero)
                {
                    LinuxX11Native.XRecordFreeContext(controlDisplay, context);
                }
            }
            catch
            {
                // ignore
            }

            context = IntPtr.Zero;

            try
            {
                if (dataDisplay != IntPtr.Zero)
                {
                    LinuxX11Native.XCloseDisplay(dataDisplay);
                }
            }
            catch
            {
                // ignore
            }

            dataDisplay = IntPtr.Zero;

            try
            {
                if (controlDisplay != IntPtr.Zero)
                {
                    LinuxX11Native.XCloseDisplay(controlDisplay);
                }
            }
            catch
            {
                // ignore
            }

            controlDisplay = IntPtr.Zero;
        }

        private void CacheKeysyms(IntPtr display)
        {
            if (LinuxX11Native.XDisplayKeycodes(display, out var min, out var max) == 0 || max < min)
            {
                keysymsByKeycode = Array.Empty<ulong>();
                return;
            }

            var table = new ulong[Math.Max(max + 1, 256)];
            var lo = Math.Max(min, 0);
            var hi = Math.Min(max, table.Length - 1);
            for (var keycode = lo; keycode <= hi; keycode++)
            {
                table[keycode] = LinuxX11Native.XKeycodeToKeysym(display, (uint)keycode, 0);
            }

            keysymsByKeycode = table;
        }

        private void OnIntercept(IntPtr closure, IntPtr recordedDataPtr)
        {
            _ = closure;
            if (recordedDataPtr == IntPtr.Zero)
            {
                return;
            }

            if (!running)
            {
                try
                {
                    LinuxX11Native.XRecordFreeData(recordedDataPtr);
                }
                catch
                {
                    // ignore
                }

                return;
            }

            try
            {
                var data = Marshal.PtrToStructure<LinuxX11Native.XRecordInterceptData>(recordedDataPtr);
                if (data.category == LinuxX11Native.XRecordStartOfData)
                {
                    ready.Set();
                    return;
                }

                // data_len is in 4-byte units; a core device event is 32 bytes (8 units).
                if (data.category != LinuxX11Native.XRecordFromServer || data.data == IntPtr.Zero || data.dataLen < 8)
                {
                    return;
                }

                var ev = Marshal.PtrToStructure<LinuxX11Native.XRecordWireEvent>(data.data);
                var eventType = (int)(ev.type & 0x7F);
                switch (eventType)
                {
                    case LinuxX11Native.KeyPress:
                    case LinuxX11Native.KeyRelease:
                    {
                        // Do not call Xlib on dataDisplay from this callback — EnableContext holds that lock.
                        var keysym = ev.detail < keysymsByKeycode.Length
                            ? keysymsByKeycode[ev.detail]
                            : 0UL;
                        var vk = LinuxKeyMap.KeySymToVk(keysym);
                        var snap = new LinuxKeySnapshot(
                            vk,
                            eventType == LinuxX11Native.KeyPress ? 0 : 1,
                            keysym);
                        onKey?.Invoke(snap);
                        break;
                    }
                    case LinuxX11Native.ButtonPress:
                    case LinuxX11Native.ButtonRelease:
                    {
                        var message = MapButton(ev.detail, eventType == LinuxX11Native.ButtonPress);
                        if (message.HasValue)
                        {
                            onMouse?.Invoke(new MouseSnapshot(
                                message.Value,
                                new Point(ev.rootX, ev.rootY),
                                ev.detail));
                        }

                        break;
                    }
                    case LinuxX11Native.MotionNotify:
                    {
                        onMouse?.Invoke(new MouseSnapshot(
                            MouseMessages.WM_MOUSEMOVE,
                            new Point(ev.rootX, ev.rootY),
                            0));
                        break;
                    }
                }
            }
            catch
            {
                // never throw from XRecord callback
            }
            finally
            {
                try
                {
                    LinuxX11Native.XRecordFreeData(recordedDataPtr);
                }
                catch
                {
                    // ignore
                }
            }
        }

        private static MouseMessages? MapButton(uint button, bool pressed)
        {
            switch (button)
            {
                case 1:
                    return pressed ? MouseMessages.WM_LBUTTONDOWN : MouseMessages.WM_LBUTTONUP;
                case 2:
                    return pressed ? MouseMessages.WM_WHEELBUTTONDOWN : MouseMessages.WM_WHEELBUTTONUP;
                case 3:
                    return pressed ? MouseMessages.WM_RBUTTONDOWN : MouseMessages.WM_RBUTTONUP;
                case 4:
                    return pressed ? MouseMessages.WM_MOUSEWHEEL : (MouseMessages?)null;
                case 5:
                    return pressed ? MouseMessages.WM_MOUSEWHEEL : (MouseMessages?)null;
                case 8:
                    return pressed ? MouseMessages.WM_XBUTTONDOWN : MouseMessages.WM_XBUTTONUP;
                case 9:
                    return pressed ? MouseMessages.WM_XBUTTONDOWN : MouseMessages.WM_XBUTTONUP;
                default:
                    return null;
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Stop();
            ready.Dispose();
        }
    }
}
