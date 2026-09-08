using System;
using EventHook.Helpers;

namespace EventHook
{
    /// <summary>
    /// Factory for watchers that share one message pump / synchronization context.
    /// On Windows, a live STA pump is required for keyboard, mouse, clipboard, hotkey, and application hooks.
    /// Dispose only after all watchers are stopped.
    /// </summary>
    /// <remarks>
    /// Windows: reuses the current WinForms/WPF UI thread when one exists; otherwise starts a background
    /// STA WinForms loop. Construction throws <see cref="TimeoutException"/> if that pump does not start.
    /// macOS and Linux ignore a provided HWND and run their own CFRunLoop / X11 loops.
    /// <see cref="Hooks.WindowHookEx"/> is not owned by this factory — call its Start() on a pumping thread.
    /// </remarks>
    public class EventHookFactory : IDisposable
    {
        private readonly SyncFactory syncFactory;
        private bool disposed;

        /// <summary>
        /// Create a factory that owns a background STA message pump when no UI thread is present.
        /// On Windows, prefer this overload unless a host window will dispatch <c>WM_HOTKEY</c> itself.
        /// </summary>
        public EventHookFactory()
            : this(null)
        {
        }

        /// <summary>
        /// Create a factory that uses an existing window handle for shell/hotkey messages (hosted scenarios).
        /// The handle must belong to a thread that pumps messages. EventHook does not subclass a provided
        /// HWND, so the host must dispatch <c>WM_HOTKEY</c> for <see cref="HotkeyWatcher"/>.
        /// On non-Windows TFMs the handle is ignored.
        /// </summary>
        /// <param name="messagePumpHandle">Existing message-pump HWND, or null to own a background pump.</param>
        public EventHookFactory(IntPtr? messagePumpHandle)
        {
            syncFactory = new SyncFactory(messagePumpHandle);
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
                syncFactory.Dispose();
            }
        }

        public ApplicationWatcher GetApplicationWatcher() => new ApplicationWatcher(syncFactory);

        public KeyboardWatcher GetKeyboardWatcher() => new KeyboardWatcher(syncFactory);

        public MouseWatcher GetMouseWatcher() => new MouseWatcher(syncFactory);

        public ClipboardWatcher GetClipboardWatcher() => new ClipboardWatcher(syncFactory);

        public PrintWatcher GetPrintWatcher() => new PrintWatcher(syncFactory);

        public HotkeyWatcher GetHotkeyWatcher() => new HotkeyWatcher(syncFactory);
    }
}
