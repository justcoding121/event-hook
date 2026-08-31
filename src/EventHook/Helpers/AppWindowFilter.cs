using System;
using EventHook.Hooks.Library;

namespace EventHook.Helpers
{
    /// <summary>
    /// Shared rules for deciding whether an HWND is an application or dialog window of interest.
    /// </summary>
    public static class AppWindowFilter
    {
        /// <summary>
        /// When true (default), windows without WS_SYSMENU are still considered if they are visible
        /// top-level windows or standard dialogs (#32770). Set false to restore the legacy strict filter.
        /// </summary>
        public static bool IncludeWindowsWithoutSysMenu { get; set; } = true;

        /// <summary>
        /// When true (default), MessageBox / common dialog class windows are included.
        /// </summary>
        public static bool IncludeDialogs { get; set; } = true;

        /// <summary>
        /// Optional caller-supplied filter. Return true to keep the window, false to ignore it.
        /// Evaluated after built-in rules pass.
        /// </summary>
        public static Func<IntPtr, bool> CustomFilter { get; set; }

        internal static bool IsAppWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
            {
                return false;
            }

            if (!User32.IsWindowVisible(hWnd))
            {
                return false;
            }

            var style = User32.GetWindowLongPtrSafe(hWnd, (int)GWLIndex.GWL_STYLE).ToInt64();
            var exStyle = User32.GetWindowLongPtrSafe(hWnd, (int)GWLIndex.GWL_EXSTYLE).ToInt64();

            if ((exStyle & (long)WindowStyleEx.WS_EX_TOOLWINDOW) != 0)
            {
                return false;
            }

            var className = WindowHelper.GetClassName(hWnd);
            var isDialog = string.Equals(className, "#32770", StringComparison.Ordinal);

            if (isDialog && IncludeDialogs)
            {
                return ApplyCustom(hWnd);
            }

            var hasSysMenu = (style & (long)WindowStyle.WS_SYSMENU) != 0;
            if (!hasSysMenu && !IncludeWindowsWithoutSysMenu)
            {
                return false;
            }

            var hwndOwner = User32.GetWindow(hWnd, (int)GetWindowContstants.GW_OWNER);
            if (hwndOwner != IntPtr.Zero)
            {
                var ownerStyle = User32.GetWindowLongPtrSafe(hwndOwner, (int)GWLIndex.GWL_STYLE).ToInt64();
                var ownerEx = User32.GetWindowLongPtrSafe(hwndOwner, (int)GWLIndex.GWL_EXSTYLE).ToInt64();
                var ownerLooksLikeApp =
                    (ownerStyle & ((long)WindowStyle.WS_VISIBLE | (long)WindowStyle.WS_CLIPCHILDREN)) ==
                    ((long)WindowStyle.WS_VISIBLE | (long)WindowStyle.WS_CLIPCHILDREN) &&
                    (ownerEx & (long)WindowStyleEx.WS_EX_TOOLWINDOW) == 0;

                // Owned windows that are not dialogs are usually not top-level apps.
                if (ownerLooksLikeApp && !isDialog)
                {
                    return false;
                }
            }

            return ApplyCustom(hWnd);
        }

        private static bool ApplyCustom(IntPtr hWnd)
        {
            var filter = CustomFilter;
            return filter == null || filter(hWnd);
        }
    }
}
