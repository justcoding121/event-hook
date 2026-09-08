using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using EventHook.Helpers;
using EventHook.Hooks;
using Microsoft.Win32.SafeHandles;

namespace EventHook.Platforms.Linux
{
    /// <summary>
    /// Reads <c>/dev/input/event*</c> when no X11 display is available.
    /// </summary>
    internal sealed class LinuxKeyboardMouseEvdev : IDisposable
    {
        private readonly Action<LinuxKeySnapshot> onKey;
        private readonly Action<MouseSnapshot> onMouse;
        private readonly List<Thread> threads = new List<Thread>();
        private readonly List<SafeFileHandle> handles = new List<SafeFileHandle>();
        private volatile bool running;
        private bool disposed;
        private int cursorX;
        private int cursorY;

        internal LinuxKeyboardMouseEvdev(Action<LinuxKeySnapshot> onKey, Action<MouseSnapshot> onMouse)
        {
            this.onKey = onKey;
            this.onMouse = onMouse;
        }

        internal HookStartResult Start()
        {
            if (running)
            {
                return HookStartResult.Ok();
            }

            string[] paths;
            try
            {
                paths = Directory.GetFiles("/dev/input", "event*");
            }
            catch (Exception ex)
            {
                return HookStartResult.Fail(HookFailureReason.NativeFailure, ex.Message);
            }

            if (paths.Length == 0)
            {
                return HookStartResult.Fail(
                    HookFailureReason.NativeFailure,
                    "No /dev/input/event* devices found.");
            }

            var opened = 0;
            var denied = false;
            foreach (var path in paths)
            {
                try
                {
                    var fd = Open(path, OpenReadOnly | OpenNonBlock);
                    if (fd < 0)
                    {
                        var errno = Marshal.GetLastPInvokeError();
                        if (errno == Eacces || errno == Eperm)
                        {
                            denied = true;
                        }

                        continue;
                    }

                    var handle = new SafeFileHandle(new IntPtr(fd), ownsHandle: true);
                    handles.Add(handle);
                    var captured = path;
                    var thread = new Thread(() => ReadLoop(handle, captured))
                    {
                        IsBackground = true,
                        Name = "EventHook.Linux.Evdev"
                    };
                    threads.Add(thread);
                    opened++;
                }
                catch (UnauthorizedAccessException)
                {
                    denied = true;
                }
                catch
                {
                    // skip device
                }
            }

            if (opened == 0)
            {
                return denied
                    ? PlatformSupport.PrivilegeInputGroup()
                    : HookStartResult.Fail(HookFailureReason.NativeFailure, "Could not open any /dev/input/event* device.");
            }

            running = true;
            foreach (var thread in threads)
            {
                thread.Start();
            }

            return HookStartResult.Ok();
        }

        internal void Stop()
        {
            running = false;
            foreach (var handle in handles)
            {
                try
                {
                    handle.Close();
                }
                catch
                {
                    // ignore
                }
            }

            foreach (var thread in threads)
            {
                if (thread.IsAlive)
                {
                    thread.Join(TimeSpan.FromSeconds(2));
                }
            }

            handles.Clear();
            threads.Clear();
        }

        private void ReadLoop(SafeFileHandle handle, string path)
        {
            _ = path;
            var buffer = new byte[Marshal.SizeOf<InputEvent>()];
            while (running && !handle.IsInvalid && !handle.IsClosed)
            {
                int read;
                try
                {
                    read = Read(handle, buffer, buffer.Length);
                }
                catch
                {
                    break;
                }

                if (read < 0)
                {
                    var errno = Marshal.GetLastPInvokeError();
                    if (errno == Eagain || errno == Eintr)
                    {
                        Thread.Sleep(5);
                        continue;
                    }

                    break;
                }

                if (read < buffer.Length)
                {
                    continue;
                }

                try
                {
                    var handlePin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                    InputEvent ev;
                    try
                    {
                        ev = Marshal.PtrToStructure<InputEvent>(handlePin.AddrOfPinnedObject());
                    }
                    finally
                    {
                        handlePin.Free();
                    }

                    Dispatch(ev);
                }
                catch
                {
                    // never throw from evdev thread
                }
            }
        }

        private void Dispatch(InputEvent ev)
        {
            if (ev.type == LinuxKeyMap.EvKey)
            {
                if (ev.code >= LinuxKeyMap.BtnLeft && ev.code <= LinuxKeyMap.BtnExtra)
                {
                    DispatchMouseButton(ev.code, ev.value);
                    return;
                }

                // value: 0=up 1=down 2=repeat
                if (ev.value == 2)
                {
                    return;
                }

                var vk = LinuxKeyMap.EvdevKeyToVk(ev.code);
                onKey?.Invoke(new LinuxKeySnapshot(vk, ev.value == 0 ? 1 : 0, 0));
                return;
            }

            if (ev.type == LinuxKeyMap.EvRel)
            {
                if (ev.code == LinuxKeyMap.RelX)
                {
                    cursorX += ev.value;
                    onMouse?.Invoke(new MouseSnapshot(MouseMessages.WM_MOUSEMOVE, new Point(cursorX, cursorY), 0));
                }
                else if (ev.code == LinuxKeyMap.RelY)
                {
                    cursorY += ev.value;
                    onMouse?.Invoke(new MouseSnapshot(MouseMessages.WM_MOUSEMOVE, new Point(cursorX, cursorY), 0));
                }
                else if (ev.code == LinuxKeyMap.RelWheel || ev.code == LinuxKeyMap.RelHWheel)
                {
                    onMouse?.Invoke(new MouseSnapshot(
                        MouseMessages.WM_MOUSEWHEEL,
                        new Point(cursorX, cursorY),
                        unchecked((uint)(ev.value * 120) << 16)));
                }
            }
        }

        private void DispatchMouseButton(ushort code, int value)
        {
            MouseMessages? message = null;
            switch (code)
            {
                case LinuxKeyMap.BtnLeft:
                    message = value == 0 ? MouseMessages.WM_LBUTTONUP : MouseMessages.WM_LBUTTONDOWN;
                    break;
                case LinuxKeyMap.BtnRight:
                    message = value == 0 ? MouseMessages.WM_RBUTTONUP : MouseMessages.WM_RBUTTONDOWN;
                    break;
                case LinuxKeyMap.BtnMiddle:
                    message = value == 0 ? MouseMessages.WM_WHEELBUTTONUP : MouseMessages.WM_WHEELBUTTONDOWN;
                    break;
                case LinuxKeyMap.BtnSide:
                case LinuxKeyMap.BtnExtra:
                    message = value == 0 ? MouseMessages.WM_XBUTTONUP : MouseMessages.WM_XBUTTONDOWN;
                    break;
            }

            if (message.HasValue)
            {
                onMouse?.Invoke(new MouseSnapshot(message.Value, new Point(cursorX, cursorY), code));
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
        }

        private const int OpenReadOnly = 0;
        private const int OpenNonBlock = 0x800;
        private const int Eacces = 13;
        private const int Eperm = 1;
        private const int Eagain = 11;
        private const int Eintr = 4;

        [DllImport("libc", SetLastError = true, EntryPoint = "open")]
        private static extern int Open(string pathname, int flags);

        [DllImport("libc", SetLastError = true, EntryPoint = "read")]
        private static extern int Read(SafeFileHandle fd, byte[] buffer, int count);

        [StructLayout(LayoutKind.Sequential)]
        private struct InputEvent
        {
            public long timeSec;
            public long timeUsec;
            public ushort type;
            public ushort code;
            public int value;
        }
    }
}
