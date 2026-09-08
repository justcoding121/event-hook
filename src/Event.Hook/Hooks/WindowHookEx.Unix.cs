using System;
using System.Runtime.CompilerServices;
using EventHook.Helpers;
using EventHook.Platforms.Mac;

namespace EventHook.Hooks
{
    /// <summary>
    /// Portable window event watcher (macOS AXObserver; Linux later).
    /// </summary>
    public sealed class WindowHookEx : IDisposable
    {
        private readonly object gate = new object();
        private MacWindowHookExBackend mac;
        private bool disposed;

        private EventHandler<WindowEventArgs> activated;
        private EventHandler<WindowEventArgs> minimized;
        private EventHandler<WindowEventArgs> unminimized;
        private EventHandler<WindowEventArgs> textChanged;

        public event EventHandler<WindowEventArgs> Activated
        {
            add { lock (gate) { activated += value; } }
            remove { lock (gate) { activated -= value; } }
        }

        public event EventHandler<WindowEventArgs> Minimized
        {
            add { lock (gate) { minimized += value; } }
            remove { lock (gate) { minimized -= value; } }
        }

        public event EventHandler<WindowEventArgs> Unminimized
        {
            add { lock (gate) { unminimized += value; } }
            remove { lock (gate) { unminimized -= value; } }
        }

        public event EventHandler<WindowEventArgs> TextChanged
        {
            add { lock (gate) { textChanged += value; } }
            remove { lock (gate) { textChanged -= value; } }
        }

        public bool IsRunning
        {
            get
            {
                lock (gate)
                {
                    return mac?.IsRunning == true;
                }
            }
        }

        public HookStartResult Start()
        {
            if (OperatingSystem.IsWindows())
            {
                return PlatformSupport.WindowsOnlyTfm();
            }

            if (OperatingSystem.IsMacOS())
            {
                return StartMac();
            }

            return PlatformSupport.NotSupportedYet("WindowHookEx", PlatformSupport.CurrentOsName);
        }

        public void Stop()
        {
            if (OperatingSystem.IsMacOS())
            {
                StopMac();
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
            activated = minimized = unminimized = textChanged = null;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private HookStartResult StartMac()
        {
            lock (gate)
            {
                mac ??= new MacWindowHookExBackend();
                mac.Activated += ForwardActivated;
                mac.Minimized += ForwardMinimized;
                mac.Unminimized += ForwardUnminimized;
                mac.TextChanged += ForwardTextChanged;
                return mac.Start();
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void StopMac()
        {
            lock (gate)
            {
                if (mac == null)
                {
                    return;
                }

                mac.Activated -= ForwardActivated;
                mac.Minimized -= ForwardMinimized;
                mac.Unminimized -= ForwardUnminimized;
                mac.TextChanged -= ForwardTextChanged;
                mac.Stop();
                mac.Dispose();
                mac = null;
            }
        }

        private void ForwardActivated(object sender, WindowEventArgs e) => activated?.Invoke(this, e);

        private void ForwardMinimized(object sender, WindowEventArgs e) => minimized?.Invoke(this, e);

        private void ForwardUnminimized(object sender, WindowEventArgs e) => unminimized?.Invoke(this, e);

        private void ForwardTextChanged(object sender, WindowEventArgs e) => textChanged?.Invoke(this, e);
    }

    /// <summary>
    /// The window event arguments.
    /// </summary>
    public class WindowEventArgs
    {
        public WindowEventArgs(IntPtr handle)
        {
            Handle = handle;
        }

        public IntPtr Handle { get; }
    }
}
