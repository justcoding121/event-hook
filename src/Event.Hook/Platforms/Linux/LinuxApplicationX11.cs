using System;
using System.Runtime.InteropServices;
using System.Text;

namespace EventHook.Platforms.Linux
{
    /// <summary>
    /// EWMH application window tracking: create/destroy + <c>_NET_ACTIVE_WINDOW</c>.
    /// Callbacks only enqueue hwnd + event type; titles are resolved on the consumer.
    /// </summary>
    internal sealed class LinuxApplicationX11 : IDisposable
    {
        private readonly Action<LinuxWindowSnapshot> onWindow;
        private LinuxX11Display display;
        private Action<LinuxX11Native.XEvent> handler;
        private IntPtr netActiveWindow;
        private IntPtr netWmName;
        private IntPtr utf8Atom;
        private IntPtr lastActive = IntPtr.Zero;
        private bool disposed;

        internal LinuxApplicationX11(Action<LinuxWindowSnapshot> onWindow)
        {
            this.onWindow = onWindow;
        }

        internal HookStartResult Start()
        {
            var gate = LinuxSession.RequireX11("Application");
            if (!gate.Success)
            {
                return gate;
            }

            var open = LinuxX11Display.TryOpen(out display);
            if (!open.Success)
            {
                return open;
            }

            try
            {
                display.Invoke(() =>
                {
                    netActiveWindow = LinuxX11Native.XInternAtom(display.Display, "_NET_ACTIVE_WINDOW", 0);
                    netWmName = LinuxX11Native.XInternAtom(display.Display, "_NET_WM_NAME", 0);
                    utf8Atom = LinuxX11Native.XInternAtom(display.Display, "UTF8_STRING", 0);

                    LinuxX11Native.XSelectInput(
                        display.Display,
                        display.Root,
                        LinuxX11Native.PropertyChangeMask |
                        LinuxX11Native.SubstructureNotifyMask |
                        LinuxX11Native.StructureNotifyMask);

                    LinuxX11Native.XFlush(display.Display);

                    // Seed current active window.
                    var active = ReadActiveWindow();
                    if (active != IntPtr.Zero)
                    {
                        lastActive = active;
                        onWindow?.Invoke(new LinuxWindowSnapshot(active, 1));
                    }
                });

                handler = OnXEvent;
                display.AddHandler(handler);
                return HookStartResult.Ok();
            }
            catch (Exception ex)
            {
                display?.Dispose();
                display = null;
                return HookStartResult.Fail(HookFailureReason.NativeFailure, ex.Message);
            }
        }

        internal void Stop()
        {
            if (display != null && handler != null)
            {
                display.RemoveHandler(handler);
            }

            display?.Dispose();
            display = null;
            handler = null;
        }

        /// <summary>
        /// Consumer-side window title read on the X thread.
        /// </summary>
        internal string TryGetWindowTitle(IntPtr hwnd)
        {
            if (display == null || !display.IsRunning || hwnd == IntPtr.Zero)
            {
                return string.Empty;
            }

            string title = string.Empty;
            try
            {
                display.Invoke(() => { title = ReadWmName(hwnd) ?? string.Empty; });
            }
            catch
            {
                return string.Empty;
            }

            return title;
        }

        private void OnXEvent(LinuxX11Native.XEvent ev)
        {
            try
            {
                switch (ev.type)
                {
                    case LinuxX11Native.CreateNotify:
                    {
                        var created = LinuxX11Native.EventAs<LinuxX11Native.XCreateWindowEvent>(ref ev);
                        if (created.window != IntPtr.Zero && created.parent == display.Root)
                        {
                            onWindow?.Invoke(new LinuxWindowSnapshot(created.window, 0));
                        }

                        break;
                    }
                    case LinuxX11Native.DestroyNotify:
                    {
                        var destroyed = LinuxX11Native.EventAs<LinuxX11Native.XDestroyWindowEvent>(ref ev);
                        if (destroyed.window != IntPtr.Zero)
                        {
                            onWindow?.Invoke(new LinuxWindowSnapshot(destroyed.window, 2));
                        }

                        break;
                    }
                    case LinuxX11Native.PropertyNotify:
                    {
                        var prop = LinuxX11Native.EventAs<LinuxX11Native.XPropertyEvent>(ref ev);
                        if (prop.window == display.Root && prop.atom == netActiveWindow)
                        {
                            var active = ReadActiveWindow();
                            if (active != IntPtr.Zero && active != lastActive)
                            {
                                lastActive = active;
                                onWindow?.Invoke(new LinuxWindowSnapshot(active, 1));
                            }
                        }

                        break;
                    }
                }
            }
            catch
            {
                // never throw
            }
        }

        private IntPtr ReadActiveWindow()
        {
            IntPtr actualType;
            int actualFormat;
            ulong nItems;
            ulong bytesAfter;
            IntPtr prop;
            var status = LinuxX11Native.XGetWindowProperty(
                display.Display,
                display.Root,
                netActiveWindow,
                0,
                1,
                0,
                IntPtr.Zero,
                out actualType,
                out actualFormat,
                out nItems,
                out bytesAfter,
                out prop);

            if (status != 0 || prop == IntPtr.Zero || nItems == 0)
            {
                if (prop != IntPtr.Zero)
                {
                    LinuxX11Native.XFree(prop);
                }

                return IntPtr.Zero;
            }

            try
            {
                return Marshal.ReadIntPtr(prop);
            }
            finally
            {
                LinuxX11Native.XFree(prop);
            }
        }

        private string ReadWmName(IntPtr hwnd)
        {
            IntPtr actualType;
            int actualFormat;
            ulong nItems;
            ulong bytesAfter;
            IntPtr prop;
            var status = LinuxX11Native.XGetWindowProperty(
                display.Display,
                hwnd,
                netWmName,
                0,
                1024,
                0,
                utf8Atom,
                out actualType,
                out actualFormat,
                out nItems,
                out bytesAfter,
                out prop);

            if (status != 0 || prop == IntPtr.Zero || nItems == 0)
            {
                if (prop != IntPtr.Zero)
                {
                    LinuxX11Native.XFree(prop);
                }

                // Fallback WM_NAME (XA_STRING = 31)
                status = LinuxX11Native.XGetWindowProperty(
                    display.Display,
                    hwnd,
                    (IntPtr)39, // XA_WM_NAME
                    0,
                    1024,
                    0,
                    IntPtr.Zero,
                    out actualType,
                    out actualFormat,
                    out nItems,
                    out bytesAfter,
                    out prop);

                if (status != 0 || prop == IntPtr.Zero || nItems == 0)
                {
                    if (prop != IntPtr.Zero)
                    {
                        LinuxX11Native.XFree(prop);
                    }

                    return null;
                }
            }

            try
            {
                if (actualFormat == 8)
                {
                    var bytes = new byte[nItems];
                    Marshal.Copy(prop, bytes, 0, (int)nItems);
                    return Encoding.UTF8.GetString(bytes).TrimEnd('\0');
                }

                return null;
            }
            finally
            {
                LinuxX11Native.XFree(prop);
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
    }
}
