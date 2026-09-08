namespace EventHook.Hooks
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Runtime.InteropServices;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;
    using EventHook.Helpers;

    /// <summary>
    /// Track events across all windows. Call <see cref="Start"/> before events fire.
    /// </summary>
    public sealed class WindowHookEx : IDisposable
    {
        private readonly WinEventProc proc;
        private readonly SortedDictionary<WindowEvent, IntPtr> hooks = new SortedDictionary<WindowEvent, IntPtr>();
        private readonly object gate = new object();
        private EventOffload<WindowEventItem> offload;
        private CancellationTokenSource taskCancellationTokenSource;
        private bool isRunning;
        private bool disposed;

        private EventHandler<WindowEventArgs> activated;
        private EventHandler<WindowEventArgs> minimized;
        private EventHandler<WindowEventArgs> unminimized;
        private EventHandler<WindowEventArgs> textChanged;

        /// <summary>
        /// Create a window event watcher. Call <see cref="Start"/> from a UI / message-pump thread.
        /// </summary>
        public WindowHookEx()
        {
            proc = Hook;
        }

        /// <summary>
        /// True only after a successful <see cref="Start"/>.
        /// </summary>
        public bool IsRunning
        {
            get
            {
                lock (gate)
                {
                    return isRunning;
                }
            }
        }

        /// <summary>
        /// Occurs when a window is about to be activated.
        /// </summary>
        public event EventHandler<WindowEventArgs> Activated
        {
            add
            {
                lock (gate)
                {
                    activated += value;
                }
            }
            remove
            {
                lock (gate)
                {
                    activated -= value;
                }
            }
        }

        /// <summary>
        /// Occurs when a window is about to be minimized.
        /// </summary>
        public event EventHandler<WindowEventArgs> Minimized
        {
            add
            {
                lock (gate)
                {
                    minimized += value;
                }
            }
            remove
            {
                lock (gate)
                {
                    minimized -= value;
                }
            }
        }

        /// <summary>
        /// Occurs when a window is about to be restored from minimized state.
        /// </summary>
        public event EventHandler<WindowEventArgs> Unminimized
        {
            add
            {
                lock (gate)
                {
                    unminimized += value;
                }
            }
            remove
            {
                lock (gate)
                {
                    unminimized -= value;
                }
            }
        }

        /// <summary>
        /// Occurs when window's text is changed.
        /// </summary>
        public event EventHandler<WindowEventArgs> TextChanged
        {
            add
            {
                lock (gate)
                {
                    textChanged += value;
                }
            }
            remove
            {
                lock (gate)
                {
                    textChanged -= value;
                }
            }
        }

        /// <summary>
        /// Install all window event hooks. Events only fire after a successful start.
        /// </summary>
        public HookStartResult Start()
        {
            lock (gate)
            {
                if (isRunning)
                {
                    return HookStartResult.Ok();
                }

                var events = new[]
                {
                    WindowEvent.ForegroundChanged,
                    WindowEvent.NameChanged,
                    WindowEvent.Minimized,
                    WindowEvent.Unmiminized
                };

                foreach (var @event in events)
                {
                    var hookId = SetWinEventHook(
                        hookMin: @event, hookMax: @event,
                        moduleHandle: IntPtr.Zero, callback: proc,
                        processID: 0, threadID: 0,
                        flags: HookFlags.OutOfContext);

                    if (hookId == IntPtr.Zero)
                    {
                        var error = Marshal.GetLastWin32Error();
                        TeardownHooksUnlocked();
                        return HookStartResult.Fail(
                            HookFailureReason.NativeFailure,
                            $"SetWinEventHook({@event}) failed. Win32 error: {error}.");
                    }

                    hooks[@event] = hookId;
                }

                taskCancellationTokenSource = new CancellationTokenSource();
                offload = new EventOffload<WindowEventItem>();
                Task.Factory.StartNew(ConsumeAsync, TaskCreationOptions.LongRunning);
                isRunning = true;
                return HookStartResult.Ok();
            }
        }

        /// <summary>
        /// Uninstall hooks and stop raising events.
        /// </summary>
        public void Stop()
        {
            lock (gate)
            {
                if (!isRunning)
                {
                    return;
                }

                isRunning = false;
                TeardownHooksUnlocked();
                offload?.Complete();
                taskCancellationTokenSource?.Cancel();
                taskCancellationTokenSource?.Dispose();
                taskCancellationTokenSource = null;
                offload?.Dispose();
                offload = null;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Stop();
            activated = minimized = unminimized = textChanged = null;
            GC.SuppressFinalize(this);
        }

        ~WindowHookEx()
        {
            try
            {
                Stop();
            }
            catch
            {
                // best-effort
            }
        }

        private void Hook(IntPtr hookHandle, WindowEvent @event,
            IntPtr hwnd,
            int @object, int child,
            int threadID, int timestampMs)
        {
            if (!isRunning)
            {
                return;
            }

            try
            {
                offload?.TryWrite(new WindowEventItem(@event, hwnd));
            }
            catch
            {
                // never throw from win event
            }
        }

        private async Task ConsumeAsync()
        {
            var token = taskCancellationTokenSource.Token;
            while (!token.IsCancellationRequested)
            {
                WindowEventItem item;
                try
                {
                    item = await offload.ReadAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ChannelClosedException)
                {
                    break;
                }
                catch (InvalidOperationException)
                {
                    break;
                }

                EventHandler<WindowEventArgs> handler;
                lock (gate)
                {
                    switch (item.Event)
                    {
                        case WindowEvent.ForegroundChanged:
                            handler = activated;
                            break;
                        case WindowEvent.NameChanged:
                            handler = textChanged;
                            break;
                        case WindowEvent.Minimized:
                            handler = minimized;
                            break;
                        case WindowEvent.Unmiminized:
                            handler = unminimized;
                            break;
                        default:
                            Debug.Write($"Unexpected event {item.Event}");
                            continue;
                    }
                }

                try
                {
                    handler?.Invoke(this, new WindowEventArgs(item.Hwnd));
                }
                catch
                {
                    // swallow user exceptions
                }
            }
        }

        private void TeardownHooksUnlocked()
        {
            foreach (var hook in hooks.Values)
            {
                if (!UnhookWinEvent(hook))
                {
                    var error = new System.ComponentModel.Win32Exception();
                    if (error.NativeErrorCode != 0x6)
                    {
                        Debug.WriteLine($"UnhookWinEvent failed: {error.Message}");
                    }
                }
            }

            hooks.Clear();
            GC.KeepAlive(proc);
        }

        private readonly struct WindowEventItem
        {
            internal WindowEventItem(WindowEvent @event, IntPtr hwnd)
            {
                Event = @event;
                Hwnd = hwnd;
            }

            internal WindowEvent Event { get; }
            internal IntPtr Hwnd { get; }
        }

        #region Native API

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWinEventHook(WindowEvent hookMin, WindowEvent hookMax,
            IntPtr moduleHandle,
            WinEventProc callback, int processID, int threadID, HookFlags flags);

        [Flags]
        private enum HookFlags : int
        {
            OutOfContext = 0,
        }

        private enum WindowEvent
        {
            ForegroundChanged = 0x03,
            NameChanged = 0x800C,
            Minimized = 0x0016,
            Unmiminized = 0x0017,
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWinEvent(IntPtr hhk);

        private delegate void WinEventProc(IntPtr hookHandle, WindowEvent @event,
            IntPtr hwnd,
            int @object, int child,
            int threadID, int timestampMs);

        #endregion
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
