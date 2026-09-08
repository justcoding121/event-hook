using System;
using System.Threading;
using System.Threading.Tasks;
using EventHook.Helpers;
using EventHook.Hooks;

#if WINDOWS
using System.Threading.Channels;
#else
using System.Threading.Channels;
using EventHook.Platforms.Linux;
using EventHook.Platforms.Mac;
#endif

namespace EventHook
{
    public class MouseEventArgs : EventArgs
    {
        public MouseMessages Message { get; set; }
        public Point Point { get; set; }
        public uint MouseData { get; set; }
    }

    /// <summary>
    /// Low-level mouse watcher with optional mouse-move filtering.
    /// </summary>
    public class MouseWatcher : IDisposable
    {
        private readonly object accesslock = new object();
#if WINDOWS
        private readonly SyncFactory factory;
#endif
        private bool isRunning;
        private bool disposed;

        /// <summary>
        /// When false, WM_MOUSEMOVE events are not enqueued (buttons/wheel still are). Default false.
        /// </summary>
        public bool IncludeMouseMove { get; set; }

#if WINDOWS
        private MouseHook mouseHook;
        private EventOffload<MouseSnapshot> offload;
        private CancellationTokenSource taskCancellationTokenSource;
#else
        private EventOffload<MouseSnapshot> linuxOffload;
        private EventOffload<MacMouseSnapshot> macOffload;
        private CancellationTokenSource taskCancellationTokenSource;
#endif

        internal MouseWatcher(SyncFactory factory)
        {
#if WINDOWS
            this.factory = factory;
#else
            _ = factory;
#endif
        }

#pragma warning disable CS0067
        public event EventHandler<MouseEventArgs> OnMouseInput;
#pragma warning restore CS0067

        /// <summary>
        /// True only after a successful <see cref="Start"/>.
        /// </summary>
        public bool IsRunning
        {
            get
            {
                lock (accesslock)
                {
                    return isRunning;
                }
            }
        }

        public HookStartResult Start()
        {
            lock (accesslock)
            {
                if (isRunning)
                {
                    return HookStartResult.Ok();
                }

#if WINDOWS
                taskCancellationTokenSource = new CancellationTokenSource();
                offload = new EventOffload<MouseSnapshot>(
                    capacity: 1024,
                    isCoalesceCandidate: s => s.Message == MouseMessages.WM_MOUSEMOVE,
                    coalesce: (_, newer) => newer);

                HookStartResult installResult = HookStartResult.Ok();
                factory.RunOnPump(() =>
                {
                    mouseHook = new MouseHook();
                    installResult = mouseHook.Start(snapshot =>
                    {
                        if (!MouseMessageFilter.ShouldRaise(snapshot.Message, IncludeMouseMove))
                        {
                            return;
                        }

                        offload?.TryWrite(snapshot);
                    });
                });

                if (!installResult.Success)
                {
                    mouseHook?.Dispose();
                    mouseHook = null;
                    offload?.Dispose();
                    offload = null;
                    taskCancellationTokenSource.Dispose();
                    taskCancellationTokenSource = null;
                    return installResult;
                }

                Task.Factory.StartNew(ConsumeAsync, TaskCreationOptions.LongRunning);
                isRunning = true;
                return HookStartResult.Ok();
#else
                if (OperatingSystem.IsWindows())
                {
                    return PlatformSupport.WindowsOnlyTfm();
                }

                if (OperatingSystem.IsMacOS())
                {
                    taskCancellationTokenSource = new CancellationTokenSource();
                    macOffload = new EventOffload<MacMouseSnapshot>(
                        capacity: 1024,
                        isCoalesceCandidate: s => s.Message == MouseMessages.WM_MOUSEMOVE,
                        coalesce: (_, newer) => newer);

                    var includeMove = IncludeMouseMove;
                    var macResult = MacKeyboardMouseHub.Shared.StartMouse(
                        snap =>
                        {
                            if (!MouseMessageFilter.ShouldRaise(snap.Message, includeMove))
                            {
                                return;
                            }

                            macOffload?.TryWrite(snap);
                        },
                        includeMove);

                    if (!macResult.Success)
                    {
                        macOffload?.Dispose();
                        macOffload = null;
                        taskCancellationTokenSource.Dispose();
                        taskCancellationTokenSource = null;
                        return macResult;
                    }

                    Task.Factory.StartNew(ConsumeMacAsync, TaskCreationOptions.LongRunning);
                    isRunning = true;
                    return HookStartResult.Ok();
                }

                if (OperatingSystem.IsLinux())
                {
                    taskCancellationTokenSource = new CancellationTokenSource();
                    linuxOffload = new EventOffload<MouseSnapshot>(
                        capacity: 1024,
                        isCoalesceCandidate: s => s.Message == MouseMessages.WM_MOUSEMOVE,
                        coalesce: (_, newer) => newer);

                    var includeMove = IncludeMouseMove;
                    var linuxResult = LinuxKeyboardMouseHub.Shared.StartMouse(
                        snap =>
                        {
                            if (!MouseMessageFilter.ShouldRaise(snap.Message, includeMove))
                            {
                                return;
                            }

                            linuxOffload?.TryWrite(snap);
                        });

                    if (!linuxResult.Success)
                    {
                        linuxOffload?.Dispose();
                        linuxOffload = null;
                        taskCancellationTokenSource.Dispose();
                        taskCancellationTokenSource = null;
                        return linuxResult;
                    }

                    Task.Factory.StartNew(ConsumeLinuxAsync, TaskCreationOptions.LongRunning);
                    isRunning = true;
                    return HookStartResult.Ok();
                }

                return PlatformSupport.NotSupportedYet("Mouse", PlatformSupport.CurrentOsName);
#endif
            }
        }

        public void Stop()
        {
            lock (accesslock)
            {
                if (!isRunning)
                {
                    return;
                }

#if WINDOWS
                factory.RunOnPump(() =>
                {
                    if (mouseHook != null)
                    {
                        mouseHook.Stop();
                        mouseHook.Dispose();
                        mouseHook = null;
                    }
                });

                isRunning = false;
                offload?.Complete();
                taskCancellationTokenSource?.Cancel();
                taskCancellationTokenSource?.Dispose();
                taskCancellationTokenSource = null;
                offload?.Dispose();
                offload = null;
#else
                if (OperatingSystem.IsMacOS())
                {
                    MacKeyboardMouseHub.Shared.StopMouse();
                    macOffload?.Complete();
                    macOffload?.Dispose();
                    macOffload = null;
                }
                else if (OperatingSystem.IsLinux())
                {
                    LinuxKeyboardMouseHub.Shared.StopMouse();
                    linuxOffload?.Complete();
                    linuxOffload?.Dispose();
                    linuxOffload = null;
                }

                isRunning = false;
                taskCancellationTokenSource?.Cancel();
                taskCancellationTokenSource?.Dispose();
                taskCancellationTokenSource = null;
#endif
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (disposing)
            {
                Stop();
            }
        }

#if WINDOWS
        private async Task ConsumeAsync()
        {
            var token = taskCancellationTokenSource.Token;
            while (!token.IsCancellationRequested)
            {
                MouseSnapshot snapshot;
                try
                {
                    snapshot = await offload.ReadAsync(token).ConfigureAwait(false);
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

                try
                {
                    OnMouseInput?.Invoke(this, new MouseEventArgs
                    {
                        Message = snapshot.Message,
                        Point = snapshot.Point,
                        MouseData = snapshot.MouseData
                    });
                }
                catch
                {
                    // swallow
                }
            }
        }
#else
        private async Task ConsumeMacAsync()
        {
            var token = taskCancellationTokenSource.Token;
            while (!token.IsCancellationRequested)
            {
                MacMouseSnapshot snapshot;
                try
                {
                    snapshot = await macOffload.ReadAsync(token).ConfigureAwait(false);
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

                try
                {
                    OnMouseInput?.Invoke(this, new MouseEventArgs
                    {
                        Message = snapshot.Message,
                        Point = snapshot.Point,
                        MouseData = snapshot.MouseData
                    });
                }
                catch
                {
                    // swallow
                }
            }
        }

        private async Task ConsumeLinuxAsync()
        {
            var token = taskCancellationTokenSource.Token;
            while (!token.IsCancellationRequested)
            {
                MouseSnapshot snapshot;
                try
                {
                    snapshot = await linuxOffload.ReadAsync(token).ConfigureAwait(false);
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

                try
                {
                    OnMouseInput?.Invoke(this, new MouseEventArgs
                    {
                        Message = snapshot.Message,
                        Point = snapshot.Point,
                        MouseData = snapshot.MouseData
                    });
                }
                catch
                {
                    // swallow
                }
            }
        }
#endif
    }
}
