#if !WINDOWS
using System;
using System.Runtime.InteropServices;

namespace EventHook.E2ETests
{
    /// <summary>
    /// Injects XTEST key/button events so Linux e2e can verify XRecord and XGrabKey delivery.
    /// </summary>
    internal static class LinuxX11Inject
    {
        private const ulong XK_a = 0x0061;
        private const ulong XK_F11 = 0xffc8;
        private const ulong XK_Shift_L = 0xffe1;
        private const ulong XK_Control_L = 0xffe3;
        private const ulong XK_Alt_L = 0xffe9;

        internal static bool DisplayAvailable =>
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY"));

        internal static bool TryFakeKeyA()
        {
            return WithDisplay(dpy =>
            {
                var keycode = XKeysymToKeycode(dpy, XK_a);
                if (keycode == 0)
                {
                    return false;
                }

                XTestFakeKeyEvent(dpy, keycode, 1, 0);
                XTestFakeKeyEvent(dpy, keycode, 0, 0);
                XSync(dpy, 0);
                return true;
            });
        }

        internal static bool TryFakeLeftClick()
        {
            return WithDisplay(dpy =>
            {
                XTestFakeButtonEvent(dpy, 1, 1, 0);
                XTestFakeButtonEvent(dpy, 1, 0, 0);
                XSync(dpy, 0);
                return true;
            });
        }

        internal static bool TryFakeHotkeyCtrlAltShiftF11()
        {
            return WithDisplay(dpy =>
            {
                var ctrl = XKeysymToKeycode(dpy, XK_Control_L);
                var alt = XKeysymToKeycode(dpy, XK_Alt_L);
                var shift = XKeysymToKeycode(dpy, XK_Shift_L);
                var f11 = XKeysymToKeycode(dpy, XK_F11);
                if (ctrl == 0 || alt == 0 || shift == 0 || f11 == 0)
                {
                    return false;
                }

                XTestFakeKeyEvent(dpy, ctrl, 1, 0);
                XTestFakeKeyEvent(dpy, alt, 1, 0);
                XTestFakeKeyEvent(dpy, shift, 1, 0);
                XTestFakeKeyEvent(dpy, f11, 1, 0);
                XTestFakeKeyEvent(dpy, f11, 0, 0);
                XTestFakeKeyEvent(dpy, shift, 0, 0);
                XTestFakeKeyEvent(dpy, alt, 0, 0);
                XTestFakeKeyEvent(dpy, ctrl, 0, 0);
                XSync(dpy, 0);
                return true;
            });
        }

        private static bool WithDisplay(Func<IntPtr, bool> action)
        {
            var dpy = XOpenDisplay(null);
            if (dpy == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                return action(dpy);
            }
            finally
            {
                XCloseDisplay(dpy);
            }
        }

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr XOpenDisplay(string displayName);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        private static extern int XCloseDisplay(IntPtr display);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        private static extern int XSync(IntPtr display, int discard);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        private static extern uint XKeysymToKeycode(IntPtr display, ulong keysym);

        [DllImport("libXtst.so.6", CallingConvention = CallingConvention.Cdecl)]
        private static extern int XTestFakeKeyEvent(IntPtr display, uint keycode, int isPress, ulong delay);

        [DllImport("libXtst.so.6", CallingConvention = CallingConvention.Cdecl)]
        private static extern int XTestFakeButtonEvent(IntPtr display, uint button, int isPress, ulong delay);
    }
}
#endif
