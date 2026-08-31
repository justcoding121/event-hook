using System;
using System.Collections.Generic;
using System.Windows.Forms;
using EventHook.Helpers;
using EventHook.Hooks.Library;

namespace EventHook
{
    /// <summary>
    /// Arguments for a registered hotkey press.
    /// </summary>
    public class HotkeyEventArgs : EventArgs
    {
        public object Id { get; set; }
        public Keys Keys { get; set; }
    }

    /// <summary>
    /// Global hotkey watcher using RegisterHotKey / WM_HOTKEY on the shared message pump.
    /// </summary>
    public class HotkeyWatcher : IDisposable
    {
        private readonly object accesslock = new object();
        private readonly SyncFactory factory;
        private readonly Dictionary<int, (object Id, Keys Keys)> registrations = new Dictionary<int, (object, Keys)>();
        private int nextId = 1;
        private bool isRunning;
        private bool disposed;
        private MessageHandler pumpWindow;

        internal HotkeyWatcher(SyncFactory factory)
        {
            this.factory = factory;
        }

        public event EventHandler<HotkeyEventArgs> OnHotkeyPressed;

        /// <summary>
        /// Register a hotkey. The same <paramref name="id"/> is returned in <see cref="OnHotkeyPressed"/>.
        /// </summary>
        public void Register(object id, Keys keys)
        {
            if (id == null)
            {
                throw new ArgumentNullException(nameof(id));
            }

            EnsureStarted();

            lock (accesslock)
            {
                var nativeId = nextId++;
                var (modifiers, vk) = Split(keys);

                factory.RunOnPump(() =>
                {
                    var hwnd = factory.GetMessageHandler()?.Handle ?? factory.GetHandle();
                    if (!User32.RegisterHotKey(hwnd, nativeId, modifiers, vk))
                    {
                        throw new InvalidOperationException(
                            $"Failed to register hotkey {keys}. Win32 error: {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");
                    }
                });

                registrations[nativeId] = (id, keys);
            }
        }

        /// <summary>
        /// Register a hotkey with a typed identifier.
        /// </summary>
        public void Register<T>(T id, Keys keys) => Register((object)id, keys);

        /// <summary>
        /// Unregister by the same id used in <see cref="Register"/>.
        /// </summary>
        public void Unregister(object id)
        {
            lock (accesslock)
            {
                int? nativeId = null;
                foreach (var pair in registrations)
                {
                    if (Equals(pair.Value.Id, id))
                    {
                        nativeId = pair.Key;
                        break;
                    }
                }

                if (nativeId == null)
                {
                    return;
                }

                factory.RunOnPump(() =>
                {
                    var hwnd = factory.GetMessageHandler()?.Handle ?? factory.GetHandle();
                    User32.UnregisterHotKey(hwnd, nativeId.Value);
                });
                registrations.Remove(nativeId.Value);
            }
        }

        public void Start() => EnsureStarted();

        public void Stop()
        {
            lock (accesslock)
            {
                if (!isRunning)
                {
                    return;
                }

                factory.RunOnPump(() =>
                {
                    var hwnd = factory.GetMessageHandler()?.Handle ?? factory.GetHandle();
                    foreach (var id in registrations.Keys)
                    {
                        User32.UnregisterHotKey(hwnd, id);
                    }

                    if (pumpWindow != null)
                    {
                        pumpWindow.MessageReceived -= OnPumpMessage;
                    }
                });

                registrations.Clear();
                isRunning = false;
            }
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

        private void EnsureStarted()
        {
            lock (accesslock)
            {
                if (isRunning)
                {
                    return;
                }

                factory.RunOnPump(() =>
                {
                    pumpWindow = factory.GetMessageHandler();
                    if (pumpWindow != null)
                    {
                        pumpWindow.MessageReceived += OnPumpMessage;
                    }
                });

                isRunning = true;
            }
        }

        private void OnPumpMessage(Message msg)
        {
            if (msg.Msg != HotkeyNative.WM_HOTKEY)
            {
                return;
            }

            var nativeId = msg.WParam.ToInt32();
            (object Id, Keys Keys) entry;
            lock (accesslock)
            {
                if (!registrations.TryGetValue(nativeId, out entry))
                {
                    return;
                }
            }

            try
            {
                OnHotkeyPressed?.Invoke(this, new HotkeyEventArgs { Id = entry.Id, Keys = entry.Keys });
            }
            catch
            {
                // never throw from message path
            }
        }

        private static (uint modifiers, uint vk) Split(Keys keys)
        {
            uint modifiers = 0;
            if (keys.HasFlag(Keys.Alt))
            {
                modifiers |= HotkeyNative.MOD_ALT;
            }

            if (keys.HasFlag(Keys.Control))
            {
                modifiers |= HotkeyNative.MOD_CONTROL;
            }

            if (keys.HasFlag(Keys.Shift))
            {
                modifiers |= HotkeyNative.MOD_SHIFT;
            }

            if (keys.HasFlag(Keys.LWin) || keys.HasFlag(Keys.RWin))
            {
                modifiers |= HotkeyNative.MOD_WIN;
            }

            var vk = (uint)(keys & Keys.KeyCode);
            return (modifiers, vk);
        }
    }
}
