using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace EventHook.Platforms.Linux
{
    /// <summary>
    /// XFixes selection-owner notifications for CLIPBOARD. Callbacks only enqueue a generation token;
    /// text is read on the consumer via <see cref="TryReadUnicodeText"/>.
    /// </summary>
    internal sealed class LinuxClipboardX11 : IDisposable
    {
        private readonly Action<LinuxClipboardSnapshot> onChanged;
        private LinuxX11Display display;
        private Action<LinuxX11Native.XEvent> handler;
        private IntPtr clipboardAtom;
        private IntPtr utf8Atom;
        private IntPtr propertyAtom;
        private IntPtr helperWindow;
        private int fixesEventBase;
        private int generation;
        private bool disposed;
        private string pendingText;
        private ManualResetEventSlim selectionReady;

        internal LinuxClipboardX11(Action<LinuxClipboardSnapshot> onChanged)
        {
            this.onChanged = onChanged;
        }

        internal HookStartResult Start()
        {
            var gate = LinuxSession.RequireX11("Clipboard");
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
                    if (XFixesQueryExtensionBool(display.Display, out fixesEventBase, out _) == 0)
                    {
                        throw new InvalidOperationException("XFixes extension is not available.");
                    }

                    clipboardAtom = LinuxX11Native.XInternAtom(display.Display, "CLIPBOARD", 0);
                    utf8Atom = LinuxX11Native.XInternAtom(display.Display, "UTF8_STRING", 0);
                    propertyAtom = LinuxX11Native.XInternAtom(display.Display, "EVENTHOOK_CLIPBOARD", 0);
                    helperWindow = LinuxX11Native.XCreateSimpleWindow(
                        display.Display,
                        display.Root,
                        -10, -10, 1, 1, 0, 0, 0);

                    LinuxX11Native.XFixesSelectSelectionInput(
                        display.Display,
                        helperWindow,
                        clipboardAtom,
                        LinuxX11Native.XFixesSetSelectionOwnerNotifyMask |
                        LinuxX11Native.XFixesSelectionWindowDestroyNotifyMask |
                        LinuxX11Native.XFixesSelectionClientCloseNotifyMask);

                    LinuxX11Native.XFlush(display.Display);
                });

                selectionReady = new ManualResetEventSlim(false);
                handler = OnXEvent;
                display.AddHandler(handler);
                return HookStartResult.Ok();
            }
            catch (DllNotFoundException ex)
            {
                display?.Dispose();
                display = null;
                return HookStartResult.Fail(HookFailureReason.NativeFailure, "Missing XFixes/X11 library: " + ex.Message);
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

            if (display != null && display.IsRunning)
            {
                try
                {
                    display.Invoke(() =>
                    {
                        if (helperWindow != IntPtr.Zero)
                        {
                            LinuxX11Native.XDestroyWindow(display.Display, helperWindow);
                            helperWindow = IntPtr.Zero;
                        }
                    });
                }
                catch
                {
                    // ignore
                }
            }

            display?.Dispose();
            display = null;
            handler = null;
            selectionReady?.Dispose();
            selectionReady = null;
        }

        /// <summary>
        /// Consumer-side clipboard text read (may wait briefly on the X thread for SelectionNotify).
        /// </summary>
        internal bool TryReadUnicodeText(out string text)
        {
            text = null;
            if (display == null || !display.IsRunning || selectionReady == null)
            {
                return false;
            }

            pendingText = null;
            selectionReady.Reset();

            try
            {
                display.Invoke(() =>
                {
                    XConvertSelection(
                        display.Display,
                        clipboardAtom,
                        utf8Atom,
                        propertyAtom,
                        helperWindow,
                        IntPtr.Zero);
                    LinuxX11Native.XFlush(display.Display);
                });
            }
            catch
            {
                return false;
            }

            if (!selectionReady.Wait(TimeSpan.FromMilliseconds(400)))
            {
                return false;
            }

            text = pendingText;
            return !string.IsNullOrEmpty(text);
        }

        private void OnXEvent(LinuxX11Native.XEvent ev)
        {
            try
            {
                if (ev.type == fixesEventBase)
                {
                    var next = Interlocked.Increment(ref generation);
                    onChanged?.Invoke(new LinuxClipboardSnapshot(next));
                    return;
                }

                if (ev.type == 31) // SelectionNotify
                {
                    pendingText = ReadPropertyUtf8();
                    selectionReady?.Set();
                }
            }
            catch
            {
                // never throw from X dispatch
            }
        }

        private string ReadPropertyUtf8()
        {
            IntPtr actualType;
            int actualFormat;
            ulong nItems;
            ulong bytesAfter;
            IntPtr prop;
            var status = LinuxX11Native.XGetWindowProperty(
                display.Display,
                helperWindow,
                propertyAtom,
                0,
                1024 * 1024,
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

            try
            {
                if (actualFormat == 8)
                {
                    var bytes = new byte[nItems];
                    Marshal.Copy(prop, bytes, 0, (int)nItems);
                    return Encoding.UTF8.GetString(bytes);
                }

                return null;
            }
            finally
            {
                LinuxX11Native.XFree(prop);
            }
        }

        [DllImport("libXfixes.so.3", CallingConvention = CallingConvention.Cdecl, EntryPoint = "XFixesQueryExtension")]
        private static extern int XFixesQueryExtensionBool(IntPtr display, out int eventBase, out int errorBase);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        private static extern int XConvertSelection(
            IntPtr display,
            IntPtr selection,
            IntPtr target,
            IntPtr property,
            IntPtr requestor,
            IntPtr time);

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
