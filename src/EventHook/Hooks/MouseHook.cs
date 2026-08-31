using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

// http://stackoverflow.com/questions/11607133/global-mouse-event-handler
namespace EventHook.Hooks
{
    internal class RawMouseEventArgs : EventArgs
    {
        internal MouseMessages Message { get; set; }
        internal Point Point { get; set; }
        internal uint MouseData { get; set; }
    }

    /// <summary>
    /// The mouse messages.
    /// </summary>
    public enum MouseMessages
    {
        WM_LBUTTONDOWN = 0x0201,
        WM_LBUTTONUP = 0x0202,
        WM_MOUSEMOVE = 0x0200,
        WM_MOUSEWHEEL = 0x020A,
        WM_RBUTTONDOWN = 0x0204,
        WM_RBUTTONUP = 0x0205,
        WM_WHEELBUTTONDOWN = 0x207,
        WM_WHEELBUTTONUP = 0x208,
        WM_XBUTTONDOWN = 0x020B,
        WM_XBUTTONUP = 0x020C
    }

    /// <summary>
    /// The point co-ordinate.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public readonly int x;
        public readonly int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSLLHOOKSTRUCT
    {
        internal Point pt;
        internal readonly uint mouseData;
        internal readonly uint flags;
        internal readonly uint time;
        internal readonly IntPtr dwExtraInfo;
    }

    internal class MouseHook
    {
        private const int WH_MOUSE_LL = 14;
        private static IntPtr _hookId = IntPtr.Zero;

        private readonly LowLevelMouseProc Proc;

        internal MouseHook()
        {
            Proc = HookCallback;
        }

        internal event EventHandler<RawMouseEventArgs> MouseAction = delegate { };

        internal void Start()
        {
            _hookId = SetHook(Proc);
        }

        internal void Stop()
        {
            UnhookWindowsHookEx(_hookId);
        }

        private static IntPtr SetHook(LowLevelMouseProc proc)
        {
            var hook = SetWindowsHookEx(WH_MOUSE_LL, proc, GetModuleHandle("user32"), 0);
            if (hook == IntPtr.Zero)
            {
                throw new Win32Exception();
            }

            return hook;
        }

        private IntPtr HookCallback(
            int nCode, IntPtr wParam, IntPtr lParam)
        {
            MSLLHOOKSTRUCT hookStruct;
            if (nCode < 0)
            {
                return CallNextHookEx(_hookId, nCode, wParam, lParam);
            }

            hookStruct = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));

            MouseAction(null, new RawMouseEventArgs { Message = (MouseMessages)wParam, Point = hookStruct.pt, MouseData = hookStruct.mouseData });

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook,
            LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode,
            IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
    }
}
