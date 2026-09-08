using System;

namespace EventHook.Helpers
{
    /// <summary>
    /// Shared messages / TFM guards for unsupported platforms.
    /// </summary>
    internal static class PlatformSupport
    {
        internal static HookStartResult WindowsOnlyTfm() =>
            HookStartResult.Fail(
                HookFailureReason.NotSupportedOnPlatform,
                "EventHook Windows hooks require TargetFramework net10.0-windows. The portable net10.0 assembly is for macOS and Linux.");

        internal static HookStartResult NotSupportedYet(string feature, string os) =>
            HookStartResult.Fail(
                HookFailureReason.NotSupportedOnPlatform,
                $"{feature} is not yet implemented on {os}.");

        internal static HookStartResult WaylandNeedsX11(string feature) =>
            HookStartResult.Fail(
                HookFailureReason.NotSupportedOnPlatform,
                $"{feature} requires an X11 display (DISPLAY). Native Wayland-only sessions are not supported in EventHook v3.");

        internal static HookStartResult DisplayUnavailable() =>
            HookStartResult.Fail(
                HookFailureReason.DisplayUnavailable,
                "No display session is available (DISPLAY / WAYLAND_DISPLAY unset).");

        internal static HookStartResult PrivilegeInputGroup() =>
            HookStartResult.Fail(
                HookFailureReason.PrivilegeRequired,
                "Cannot open /dev/input/event* (permission denied). Add your user to the 'input' group (usermod -aG input $USER) and re-login.");

        internal static HookStartResult MacPermission(string permission) =>
            HookStartResult.Fail(
                HookFailureReason.PermissionDenied,
                $"macOS denied {permission}. Grant it under System Settings → Privacy & Security → {permission} for this host process (Terminal, dotnet, or your .app), then restart.");

        internal static bool IsWindowsTfm
        {
            get
            {
#if WINDOWS
                return true;
#else
                return false;
#endif
            }
        }

        internal static string CurrentOsName
        {
            get
            {
                if (OperatingSystem.IsWindows())
                {
                    return "Windows";
                }

                if (OperatingSystem.IsMacOS())
                {
                    return "macOS";
                }

                if (OperatingSystem.IsLinux())
                {
                    return "Linux";
                }

                return "this OS";
            }
        }
    }
}
