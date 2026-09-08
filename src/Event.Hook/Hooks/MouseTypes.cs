using System.Runtime.InteropServices;

namespace EventHook.Hooks
{
    /// <summary>
    /// Logical mouse message types (Win32 WM_* values on Windows; mapped equivalents on other OSes).
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
    /// Screen point.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public readonly int x;
        public readonly int y;

        public Point(int x, int y)
        {
            this.x = x;
            this.y = y;
        }

        public override string ToString() => $"{x},{y}";
    }

    /// <summary>
    /// Value-type mouse event captured on an OS hook / input thread (no decode).
    /// </summary>
    internal readonly struct MouseSnapshot
    {
        internal MouseSnapshot(MouseMessages message, Point point, uint mouseData)
        {
            Message = message;
            Point = point;
            MouseData = mouseData;
        }

        internal MouseMessages Message { get; }
        internal Point Point { get; }
        internal uint MouseData { get; }
    }
}
