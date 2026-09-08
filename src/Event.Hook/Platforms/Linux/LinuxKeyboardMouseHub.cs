using System;
using EventHook.Hooks;

namespace EventHook.Platforms.Linux
{
    /// <summary>
    /// Shared XRecord/evdev backend so keyboard + mouse watchers do not install two native hooks.
    /// </summary>
    internal sealed class LinuxKeyboardMouseHub : IDisposable
    {
        internal static readonly LinuxKeyboardMouseHub Shared = new LinuxKeyboardMouseHub();

        private readonly object gate = new object();
        private Action<LinuxKeySnapshot> keySink;
        private Action<MouseSnapshot> mouseSink;
        private IDisposable backend;
        private int keyUsers;
        private int mouseUsers;
        private bool disposed;

        internal HookStartResult StartKeyboard(Action<LinuxKeySnapshot> onKey)
        {
            if (onKey == null)
            {
                throw new ArgumentNullException(nameof(onKey));
            }

            lock (gate)
            {
                keySink = onKey;
                keyUsers++;
                var result = EnsureBackend();
                if (!result.Success)
                {
                    keyUsers--;
                    if (keyUsers == 0)
                    {
                        keySink = null;
                    }
                }

                return result;
            }
        }

        internal HookStartResult StartMouse(Action<MouseSnapshot> onMouse)
        {
            if (onMouse == null)
            {
                throw new ArgumentNullException(nameof(onMouse));
            }

            lock (gate)
            {
                mouseSink = onMouse;
                mouseUsers++;
                var result = EnsureBackend();
                if (!result.Success)
                {
                    mouseUsers--;
                    if (mouseUsers == 0)
                    {
                        mouseSink = null;
                    }
                }

                return result;
            }
        }

        internal void StopKeyboard()
        {
            lock (gate)
            {
                if (keyUsers > 0)
                {
                    keyUsers--;
                }

                if (keyUsers == 0)
                {
                    keySink = null;
                }

                MaybeStopBackend();
            }
        }

        internal void StopMouse()
        {
            lock (gate)
            {
                if (mouseUsers > 0)
                {
                    mouseUsers--;
                }

                if (mouseUsers == 0)
                {
                    mouseSink = null;
                }

                MaybeStopBackend();
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            lock (gate)
            {
                keyUsers = 0;
                mouseUsers = 0;
                keySink = null;
                mouseSink = null;
                backend?.Dispose();
                backend = null;
            }
        }

        private HookStartResult EnsureBackend()
        {
            if (backend != null)
            {
                return HookStartResult.Ok();
            }

            return LinuxKeyboardMouseFactory.Start(
                snap => keySink?.Invoke(snap),
                snap => mouseSink?.Invoke(snap),
                out backend);
        }

        private void MaybeStopBackend()
        {
            if (keyUsers > 0 || mouseUsers > 0)
            {
                return;
            }

            backend?.Dispose();
            backend = null;
        }
    }
}
