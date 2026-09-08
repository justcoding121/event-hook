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

                var clients = new[] { (IntPtr)0xffff }; // XRecordAllClients
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
            finally
            {
                ready.Set();
            }

            if (startError != null || context == IntPtr.Zero || dataDisplay == IntPtr.Zero)
            {
                CleanupDisplays();
                return;
            }

            try
            {
                // Blocks until XRecordDisableContext from the control connection.
                LinuxX11Native.XRecordEnableContext(dataDisplay, context, interceptProc, IntPtr.Zero);
            }
            catch (Exception)
            {
                // shutdown path
            }

            CleanupDisplays();
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

        private void OnIntercept(IntPtr closure, IntPtr recordedDataPtr)
        {
            _ = closure;
            if (recordedDataPtr == IntPtr.Zero || !running)
            {
                return;
            }

            try
            {
                var data = Marshal.PtrToStructure<LinuxX11Native.XRecordInterceptData>(recordedDataPtr);
                if (data.category != LinuxX11Native.XRecordFromServer || data.data == IntPtr.Zero || data.dataLen < 1)
                {
                    LinuxX11Native.XRecordFreeData(recordedDataPtr);
                    return;
                }

                // First byte is the core event type for device events.
                var eventType = Marshal.ReadByte(data.data);
                switch (eventType)
                {
                    case LinuxX11Native.KeyPress:
                    case LinuxX11Native.KeyRelease:
                    {
                        var key = Marshal.PtrToStructure<LinuxX11Native.XKeyEvent>(data.data);
                        // Callback runs on the data-display thread — only touch dataDisplay here.
                        ulong keysym = dataDisplay != IntPtr.Zero
                            ? LinuxX11Native.XKeycodeToKeysym(dataDisplay, key.keycode, 0)
                            : 0;

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
                        var btn = Marshal.PtrToStructure<LinuxX11Native.XButtonEvent>(data.data);
                        var message = MapButton(btn.button, eventType == LinuxX11Native.ButtonPress);
                        if (message.HasValue)
                        {
                            onMouse?.Invoke(new MouseSnapshot(
                                message.Value,
                                new Point(btn.x_root, btn.y_root),
                                btn.button));
                        }

                        break;
                    }
                    case LinuxX11Native.MotionNotify:
                    {
                        var motion = Marshal.PtrToStructure<LinuxX11Native.XMotionEvent>(data.data);
                        onMouse?.Invoke(new MouseSnapshot(
                            MouseMessages.WM_MOUSEMOVE,
                            new Point(motion.x_root, motion.y_root),
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
