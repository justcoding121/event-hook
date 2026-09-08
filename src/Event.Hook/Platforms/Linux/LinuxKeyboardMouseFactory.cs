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
                var x11Result = x11.Start();
                if (x11Result.Success)
                {
                    backend = x11;
                    return x11Result;
                }

                x11.Dispose();

                var fallback = new LinuxKeyboardMouseEvdev(onKey, onMouse);
                var fallbackResult = fallback.Start();
                if (fallbackResult.Success)
                {
                    backend = fallback;
                    return fallbackResult;
                }

                fallback.Dispose();
                return x11Result;
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
