using System;
using System.Diagnostics;
using System.Text;
using EventHook.Hooks.Library;

namespace EventHook.Helpers
{
    /// <summary>
    /// Helper methods for window titles, process paths, and foreground HWND.
    /// </summary>
    internal static class WindowHelper
    {
        internal static IntPtr GetActiveWindowHandle()
        {
            try
            {
                return User32.GetForegroundWindow();
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        internal static string GetAppPath(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                User32.GetWindowThreadProcessId(hWnd, out var pid);
                using var proc = Process.GetProcessById((int)pid);
                return proc.MainModule?.FileName;
            }
            catch
            {
                return null;
            }
        }

        internal static string GetWindowText(IntPtr hWnd)
        {
            try
            {
                var length = User32.GetWindowTextLength(hWnd);
                var sb = new StringBuilder(length + 1);
                User32.GetWindowText(hWnd, sb, sb.Capacity);
                return sb.ToString();
            }
            catch
            {
                return null;
            }
        }

        internal static string GetAppDescription(string appPath)
        {
            if (appPath == null)
            {
                return null;
            }

            try
            {
                return FileVersionInfo.GetVersionInfo(appPath).FileDescription;
            }
            catch
            {
                return null;
            }
        }

        internal static string GetClassName(IntPtr hWnd)
        {
            try
            {
                var sb = new StringBuilder(256);
                User32.GetClassName(hWnd, sb, sb.Capacity);
                return sb.ToString();
            }
            catch
            {
                return null;
            }
        }
    }
}
