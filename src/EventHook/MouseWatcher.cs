using System;
using System.Threading;
using System.Threading.Tasks;
using EventHook.Helpers;
using EventHook.Hooks;

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
        private readonly SyncFactory factory;
        private MouseHook mouseHook;
        private AsyncConcurrentQueue<object> mouseQueue;
        private CancellationTokenSource taskCancellationTokenSource;
        private bool isRunning;
        private bool disposed;

        /// <summary>
        /// When false, WM_MOUSEMOVE events are not raised (buttons/wheel still are). Default true.
        /// </summary>
        public bool IncludeMouseMove { get; set; } = true;

        internal MouseWatcher(SyncFactory factory)
        {
            this.factory = factory;
        }

        public event EventHandler<MouseEventArgs> OnMouseInput;

        public void Start()
        {
            lock (accesslock)
            {
                if (isRunning)
                {
                    return;
                }

                taskCancellationTokenSource = new CancellationTokenSource();
                mouseQueue = new AsyncConcurrentQueue<object>(taskCancellationTokenSource.Token);

                factory.RunOnPump(() =>
                {
                    mouseHook = new MouseHook();
                    mouseHook.MouseAction += MListener;
                    mouseHook.Start();
                });

                Task.Factory.StartNew(ConsumeAsync);
                isRunning = true;
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

                factory.RunOnPump(() =>
                {
                    if (mouseHook != null)
                    {
                        mouseHook.MouseAction -= MListener;
                        mouseHook.Stop();
                        mouseHook = null;
                    }
                });

                mouseQueue.Enqueue(false);
                isRunning = false;
                taskCancellationTokenSource.Cancel();
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

        private void MListener(object sender, RawMouseEventArgs e)
        {
            try
            {
                if (!MouseMessageFilter.ShouldRaise(e.Message, IncludeMouseMove))
                {
                    return;
                }

                mouseQueue?.Enqueue(e);
            }
            catch
            {
                // never throw from hook callback
            }
        }

        private async Task ConsumeAsync()
        {
            while (isRunning)
            {
                object item;
                try
                {
                    item = await mouseQueue.DequeueAsync();
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (item is null)
                {
                    continue;
                }

                if (item is bool)
                {
                    break;
                }

                try
                {
                    var kd = (RawMouseEventArgs)item;
                    OnMouseInput?.Invoke(this,
                        new MouseEventArgs { Message = kd.Message, Point = kd.Point, MouseData = kd.MouseData });
                }
                catch
                {
                    // swallow
                }
            }
        }
    }
}
