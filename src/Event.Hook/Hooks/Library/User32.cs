using System;
using System.Runtime.InteropServices;
using System.Text;

namespace EventHook.Hooks.Library
{
    internal static class User32
    {
        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        internal static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        internal static extern int GetWindowText(IntPtr hWnd, [Out] StringBuilder lpString, int nMaxCount);

        [DllImport("User32.dll", CharSet = CharSet.Auto)]
        internal static extern IntPtr SetClipboardViewer(IntPtr hWnd);

        [DllImport("User32.dll", CharSet = CharSet.Auto)]
        internal static extern bool ChangeClipboardChain(IntPtr hWndRemove, IntPtr hWndNewNext);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        internal static extern IntPtr SendMessage(IntPtr hwnd, int wMsg, IntPtr wParam, IntPtr lParam);

        [DllImport("User32.dll", CharSet = CharSet.Auto)]
        internal static extern bool EnumWindows(EnumWindowsProc numFunc, IntPtr lParam);

        [DllImport("User32.dll", CharSet = CharSet.Auto)]
        internal static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("User32.dll", CharSet = CharSet.Auto)]
        internal static extern IntPtr GetWindow(IntPtr hwnd, int uCmd);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true, CharSet = CharSet.Auto)]
        internal static extern IntPtr GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true, CharSet = CharSet.Auto)]
        internal static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("User32.dll", CharSet = CharSet.Auto)]
        internal static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("User32.dll", CharSet = CharSet.Auto)]
        internal static extern bool RegisterShellHook(IntPtr hWnd, int flags);

        [DllImport("User32.dll", CharSet = CharSet.Auto)]
        internal static extern bool RegisterShellHookWindow(IntPtr hWnd);

        [DllImport("User32.dll", CharSet = CharSet.Auto)]
        internal static extern uint RegisterWindowMessage(string Message);

        [DllImport("user32.dll")]
        internal static extern void SetTaskmanWindow(IntPtr hwnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        internal static extern IntPtr SetWindowsHookEx(int idHook, Delegate lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        internal static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
            WinEventProc lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        internal static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        internal static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        internal static IntPtr GetWindowLongPtrSafe(IntPtr hWnd, int nIndex)
        {
            return IntPtr.Size == 8
                ? GetWindowLongPtr(hWnd, nIndex)
                : GetWindowLong(hWnd, nIndex);
        }
    }
}
