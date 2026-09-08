using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace EventHook.Hooks.Library
{
    #region RECT struct

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        internal int left;
        internal int top;
        internal int right;
        internal int bottom;

        internal RECT(int left, int top, int right, int bottom)
        {
            this.left = left;
            this.top = top;
            this.right = right;
            this.bottom = bottom;
        }

        internal Rectangle Rect => new Rectangle(left, top, right - left, bottom - top);

        internal static RECT FromXywh(int x, int y, int width, int height)
        {
            return new RECT(x,
                y,
                x + width,
                y + height);
        }

        internal static RECT FromRectangle(Rectangle rect)
        {
            return new RECT(rect.Left,
                rect.Top,
                rect.Right,
                rect.Bottom);
        }

        public override string ToString()
        {
            return "{left=" + left + ", " + "top=" + top + ", " +
                   "right=" + right + ", " + "bottom=" + bottom + "}";
        }
    }

    #endregion //RECT

    #region SHELLHOOKINFO

    internal struct SHELLHOOKINFO
    {
        internal IntPtr Hwnd;
        internal RECT Rc;
    }

    #endregion
}
