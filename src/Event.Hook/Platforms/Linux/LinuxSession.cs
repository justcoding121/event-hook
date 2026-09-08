using System;
using EventHook.Helpers;

namespace EventHook.Platforms.Linux
{
    /// <summary>
    /// Linux display-session detection for X11 vs Wayland routing.
    /// </summary>
    internal static class LinuxSession
    {
        internal static string Display =>
            Environment.GetEnvironmentVariable("DISPLAY");

        internal static string WaylandDisplay =>
            Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");

        internal static bool HasX11Display =>
            !string.IsNullOrWhiteSpace(Display);

        internal static bool HasWaylandDisplay =>
            !string.IsNullOrWhiteSpace(WaylandDisplay);

        internal static bool IsWaylandOnly =>
            !HasX11Display && HasWaylandDisplay;

        internal static bool HasAnyDisplaySession =>
            HasX11Display || HasWaylandDisplay;

        /// <summary>
        /// Clipboard / windows / hotkeys require X11 in v3.
        /// </summary>
        internal static HookStartResult RequireX11(string feature)
        {
            if (HasX11Display)
            {
                return HookStartResult.Ok();
            }

            if (IsWaylandOnly || HasWaylandDisplay)
            {
                return PlatformSupport.WaylandNeedsX11(feature);
            }

            return PlatformSupport.DisplayUnavailable();
        }
    }
}
