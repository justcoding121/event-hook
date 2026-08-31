using System;
using EventHook.Helpers;

namespace EventHook
{
    /// <summary>
    /// Factory for watchers that share one message pump / synchronization context.
    /// Dispose only after all watchers are stopped.
    /// </summary>
    public class EventHookFactory : IDisposable
    {
        private readonly SyncFactory syncFactory;
        private bool disposed;

        /// <summary>
        /// Create a factory that owns a background STA message pump when no UI thread is present.
        /// </summary>
        public EventHookFactory()
            : this(null)
        {
        }

        /// <summary>
        /// Create a factory that uses an existing window handle for shell/hotkey messages (hosted scenarios).
        /// </summary>
        public EventHookFactory(IntPtr? messagePumpHandle)
        {
            syncFactory = new SyncFactory(messagePumpHandle);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            syncFactory.Dispose();
        }

        public ApplicationWatcher GetApplicationWatcher() => new ApplicationWatcher(syncFactory);

        public KeyboardWatcher GetKeyboardWatcher() => new KeyboardWatcher(syncFactory);

        public MouseWatcher GetMouseWatcher() => new MouseWatcher(syncFactory);

        public ClipboardWatcher GetClipboardWatcher() => new ClipboardWatcher(syncFactory);

        public PrintWatcher GetPrintWatcher() => new PrintWatcher(syncFactory);

        public HotkeyWatcher GetHotkeyWatcher() => new HotkeyWatcher(syncFactory);
    }
}
