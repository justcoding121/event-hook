using System;
using System.Collections.Generic;
using System.Threading;
using EventHook.Helpers;

namespace EventHook.Platforms.Linux
{
    /// <summary>
    /// Global hotkeys via <c>XGrabKey</c>. Conflicts surface as <see cref="HookFailureReason.AlreadyInUse"/>.
    /// </summary>
    internal sealed class LinuxHotkeyX11 : IDisposable
    {
        private readonly Action<int> onHotkey;
        private readonly Dictionary<int, (object Id, Hotkey Hotkey, int Keycode, uint Modifiers)> registrations =
            new Dictionary<int, (object, Hotkey, int, uint)>();
        private LinuxX11Display display;
        private Action<LinuxX11Native.XEvent> handler;
        private int nextId = 1;
        private bool disposed;
        private bool started;
        private LinuxX11Native.XErrorHandler previousHandler;
        private LinuxX11Native.XErrorHandler currentHandler;
        private int lastErrorCode;

        internal LinuxHotkeyX11(Action<int> onHotkey)
        {
            this.onHotkey = onHotkey;
        }

        internal HookStartResult Start()
        {
            var gate = LinuxSession.RequireX11("Hotkey");
            if (!gate.Success)
            {
                return gate;
            }

            if (started)
            {
                return HookStartResult.Ok();
            }

            var open = LinuxX11Display.TryOpen(out display);
            if (!open.Success)
            {
                return open;
            }

            try
            {
                currentHandler = OnXError;
                display.Invoke(() =>
                {
                    previousHandler = LinuxX11Native.XSetErrorHandler(currentHandler);
                    LinuxX11Native.XSelectInput(display.Display, display.Root, LinuxX11Native.KeyPressMask);
                    LinuxX11Native.XFlush(display.Display);
                });

                handler = OnXEvent;
                display.AddHandler(handler);
                started = true;
                return HookStartResult.Ok();
            }
            catch (Exception ex)
            {
                display?.Dispose();
                display = null;
                return HookStartResult.Fail(HookFailureReason.NativeFailure, ex.Message);
            }
        }

        internal HookStartResult Register(object id, Hotkey hotkey)
        {
            var start = Start();
            if (!start.Success)
            {
                return start;
            }

            var keysym = LinuxKeyMap.EventKeyToKeySym(hotkey.Key);
            if (keysym == 0)
            {
                return HookStartResult.Fail(HookFailureReason.NativeFailure, "Unsupported hotkey key: " + hotkey.Key);
            }

            var modifiers = LinuxKeyMap.ModifiersToX(hotkey.Modifiers);
            var nativeId = Interlocked.Increment(ref nextId);
            var registered = false;
            var alreadyInUse = false;
            Exception error = null;

            display.Invoke(() =>
            {
                try
                {
                    var keycode = (int)LinuxX11Native.XKeysymToKeycode(display.Display, keysym);
                    if (keycode == 0)
                    {
                        error = new InvalidOperationException("XKeysymToKeycode failed for " + hotkey.Key);
                        return;
                    }

                    lastErrorCode = 0;
                    // Grab with and without NumLock/CapsLock (LockMask / Mod2).
                    foreach (var extra in IgnoreMaskVariants(modifiers))
                    {
                        LinuxX11Native.XGrabKey(
                            display.Display,
                            keycode,
                            extra,
                            display.Root,
                            1,
                            LinuxX11Native.GrabModeAsync,
                            LinuxX11Native.GrabModeAsync);
                    }

                    LinuxX11Native.XSync(display.Display, 0);
                    if (lastErrorCode == LinuxX11Native.BadAccess)
                    {
                        alreadyInUse = true;
                        foreach (var extra in IgnoreMaskVariants(modifiers))
                        {
                            LinuxX11Native.XUngrabKey(display.Display, keycode, extra, display.Root);
                        }

                        return;
                    }

                    registrations[nativeId] = (id, hotkey, keycode, modifiers);
                    registered = true;
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            });

            if (alreadyInUse)
            {
                return HookStartResult.Fail(
                    HookFailureReason.AlreadyInUse,
                    $"Failed to register hotkey {hotkey}: already grabbed (BadAccess).");
            }

            if (error != null)
            {
                return HookStartResult.Fail(HookFailureReason.NativeFailure, error.Message);
            }

            return registered
                ? HookStartResult.Ok()
                : HookStartResult.Fail(HookFailureReason.NativeFailure, "XGrabKey failed.");
        }

        internal void Unregister(object id)
        {
            if (display == null || !started)
            {
                return;
            }

            int? nativeId = null;
            int keycode = 0;
            uint modifiers = 0;
            foreach (var pair in registrations)
            {
                if (Equals(pair.Value.Id, id))
                {
                    nativeId = pair.Key;
                    keycode = pair.Value.Keycode;
                    modifiers = pair.Value.Modifiers;
                    break;
                }
            }

            if (nativeId == null)
            {
                return;
            }

            try
            {
                display.Invoke(() =>
                {
                    foreach (var extra in IgnoreMaskVariants(modifiers))
                    {
                        LinuxX11Native.XUngrabKey(display.Display, keycode, extra, display.Root);
                    }

                    LinuxX11Native.XFlush(display.Display);
                });
            }
            catch
            {
                // ignore
            }

            registrations.Remove(nativeId.Value);
        }

        internal bool TryGetRegistration(int nativeId, out object id, out Hotkey hotkey)
        {
            if (registrations.TryGetValue(nativeId, out var entry))
            {
                id = entry.Id;
                hotkey = entry.Hotkey;
                return true;
            }

            id = null;
            hotkey = default;
            return false;
        }

        internal void Stop()
        {
            if (!started)
            {
                return;
            }

            if (display != null && handler != null)
            {
                display.RemoveHandler(handler);
            }

            if (display != null && display.IsRunning)
            {
                try
                {
                    display.Invoke(() =>
                    {
                        foreach (var pair in registrations)
                        {
                            foreach (var extra in IgnoreMaskVariants(pair.Value.Modifiers))
                            {
                                LinuxX11Native.XUngrabKey(
                                    display.Display,
                                    pair.Value.Keycode,
                                    extra,
                                    display.Root);
                            }
                        }

                        if (currentHandler != null)
                        {
                            LinuxX11Native.XSetErrorHandler(previousHandler);
                        }

                        LinuxX11Native.XFlush(display.Display);
                    });
                }
                catch
                {
                    // ignore
                }
            }

            registrations.Clear();
            display?.Dispose();
            display = null;
            handler = null;
            started = false;
        }

        private void OnXEvent(LinuxX11Native.XEvent ev)
        {
            if (ev.type != LinuxX11Native.KeyPress)
            {
                return;
            }

            try
            {
                var key = LinuxX11Native.EventAs<LinuxX11Native.XKeyEvent>(ref ev);
                var cleaned = key.state & ~(LinuxX11Native.LockMask | LinuxX11Native.Mod2Mask);
                foreach (var pair in registrations)
                {
                    if (pair.Value.Keycode == (int)key.keycode && pair.Value.Modifiers == cleaned)
                    {
                        onHotkey?.Invoke(pair.Key);
                        break;
                    }
                }
            }
            catch
            {
                // never throw
            }
        }

        private int OnXError(IntPtr dpy, ref LinuxX11Native.XErrorEvent errorEvent)
        {
            _ = dpy;
            lastErrorCode = errorEvent.error_code;
            return 0;
        }

        private static IEnumerable<uint> IgnoreMaskVariants(uint baseModifiers)
        {
            yield return baseModifiers;
            yield return baseModifiers | LinuxX11Native.LockMask;
            yield return baseModifiers | LinuxX11Native.Mod2Mask;
            yield return baseModifiers | LinuxX11Native.LockMask | LinuxX11Native.Mod2Mask;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Stop();
        }
    }
}
