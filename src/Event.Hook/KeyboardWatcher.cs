using System;
using System.Threading;
using System.Threading.Tasks;
using EventHook.Helpers;

#if WINDOWS
using System.Threading.Channels;
using EventHook.Hooks;
#endif

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
        private bool isRunning;
        private bool disposed;

#if WINDOWS
        private KeyboardHook keyboardHook;
        private EventOffload<KeyboardSnapshot> offload;
        private CancellationTokenSource taskCancellationTokenSource;
#endif

        internal KeyboardWatcher(SyncFactory factory)
        {
            this.factory = factory;
        }

#pragma warning disable CS0067 // Raised only on Windows implementation
        public event EventHandler<KeyInputEventArgs> OnKeyInput;
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
                offload = new EventOffload<KeyboardSnapshot>();

                HookStartResult installResult = HookStartResult.Ok();
                factory.RunOnPump(() =>
                {
                    keyboardHook = new KeyboardHook();
                    installResult = keyboardHook.Start(snapshot =>
                    {
                        offload?.TryWrite(snapshot);
                    });
                });

                if (!installResult.Success)
                {
                    keyboardHook?.Dispose();
                    keyboardHook = null;
                    offload?.Dispose();
                    offload = null;
                    taskCancellationTokenSource.Dispose();
                    taskCancellationTokenSource = null;
                    return installResult;
                }

                Task.Factory.StartNew(ConsumeKeyAsync, TaskCreationOptions.LongRunning);
                isRunning = true;
                return HookStartResult.Ok();
#else
                if (OperatingSystem.IsWindows())
                {
                    return PlatformSupport.WindowsOnlyTfm();
                }

                return PlatformSupport.NotSupportedYet("Keyboard", PlatformSupport.CurrentOsName);
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
                    if (keyboardHook != null)
                    {
                        keyboardHook.Stop();
                        keyboardHook.Dispose();
                        keyboardHook = null;
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
                isRunning = false;
#endif
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

#if WINDOWS
        private async Task ConsumeKeyAsync()
        {
            var token = taskCancellationTokenSource.Token;
            while (!token.IsCancellationRequested)
            {
                KeyboardSnapshot snapshot;
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
                    var unicode = KeyboardHook.VkCodeToString(snapshot, snapshot.IsKeyDown);
                    var keyData = new KeyData
                    {
                        UnicodeCharacter = unicode,
                        Keyname = VirtualKeyNames.GetName(snapshot.VkCode),
                        EventType = (KeyEvent)snapshot.EventType
                    };
                    OnKeyInput?.Invoke(this, new KeyInputEventArgs { KeyData = keyData });
                }
                catch
                {
                    // swallow user callback exceptions
                }
            }
        }
#endif
    }
}
