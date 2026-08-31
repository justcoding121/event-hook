using System;
using System.Threading;
using System.Threading.Tasks;
using EventHook.Helpers;
using EventHook.Hooks;

namespace EventHook
{
    public class KeyInputEventArgs : EventArgs
    {
        public KeyData KeyData { get; set; }
    }

    public class KeyData
    {
        public KeyEvent EventType;
        public string Keyname;
        public string UnicodeCharacter;
    }

    public enum KeyEvent
    {
        down = 0,
        up = 1
    }

    /// <summary>
    /// Low-level keyboard watcher. Always calls CallNextHookEx so layout shortcuts (e.g. Shift+Alt) keep working.
    /// Global hooks typically do not receive input inside remote desktop sessions.
    /// </summary>
    public class KeyboardWatcher : IDisposable
    {
        private readonly object accesslock = new object();
        private readonly SyncFactory factory;
        private KeyboardHook keyboardHook;
        private AsyncConcurrentQueue<object> keyQueue;
        private CancellationTokenSource taskCancellationTokenSource;
        private bool isRunning;
        private bool disposed;

        internal KeyboardWatcher(SyncFactory factory)
        {
            this.factory = factory;
        }

        public event EventHandler<KeyInputEventArgs> OnKeyInput;

        public void Start()
        {
            lock (accesslock)
            {
                if (isRunning)
                {
                    return;
                }

                taskCancellationTokenSource = new CancellationTokenSource();
                keyQueue = new AsyncConcurrentQueue<object>(taskCancellationTokenSource.Token);

                factory.RunOnPump(() =>
                {
                    keyboardHook = new KeyboardHook();
                    keyboardHook.KeyDown += KListener;
                    keyboardHook.KeyUp += KListener;
                    keyboardHook.Start();
                });

                Task.Factory.StartNew(ConsumeKeyAsync);
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
                    if (keyboardHook != null)
                    {
                        keyboardHook.KeyDown -= KListener;
                        keyboardHook.KeyUp -= KListener;
                        keyboardHook.Stop();
                        keyboardHook = null;
                    }
                });

                keyQueue.Enqueue(false);
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

        private void KListener(object sender, RawKeyEventArgs e)
        {
            try
            {
                keyQueue?.Enqueue(new KeyData
                {
                    UnicodeCharacter = e.Character,
                    Keyname = e.Key.ToString(),
                    EventType = (KeyEvent)e.EventType
                });
            }
            catch
            {
                // never throw from hook callback
            }
        }

        private async Task ConsumeKeyAsync()
        {
            while (isRunning)
            {
                object item;
                try
                {
                    item = await keyQueue.DequeueAsync();
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
                    OnKeyInput?.Invoke(this, new KeyInputEventArgs { KeyData = (KeyData)item });
                }
                catch
                {
                    // swallow user callback exceptions
                }
            }
        }
    }
}
