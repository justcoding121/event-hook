using System;

namespace EventHook.Platforms.Linux
{
    /// <summary>
    /// Chooses XRecord (DISPLAY set) or evdev for keyboard/mouse.
    /// </summary>
    internal static class LinuxKeyboardMouseFactory
    {
        internal static HookStartResult Start(
            Action<LinuxKeySnapshot> onKey,
            Action<Hooks.MouseSnapshot> onMouse,
            out IDisposable backend)
        {
            backend = null;

            if (LinuxSession.HasX11Display)
            {
                var x11 = new LinuxKeyboardMouseX11(onKey, onMouse);
                var result = x11.Start();
                if (!result.Success)
                {
                    x11.Dispose();
                    return result;
                }

                backend = x11;
                return result;
            }

            var evdev = new LinuxKeyboardMouseEvdev(onKey, onMouse);
            var evdevResult = evdev.Start();
            if (!evdevResult.Success)
            {
                evdev.Dispose();
                return evdevResult;
            }

            backend = evdev;
            return evdevResult;
        }
    }
}
