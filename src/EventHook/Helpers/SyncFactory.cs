using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Threading;

namespace EventHook.Helpers
{
    /// <summary>
    /// Creates or reuses a message pump required by Win32 hooks.
    /// Prefers an existing UI Dispatcher / SynchronizationContext to avoid ContextSwitchDeadlock in hosted apps.
    /// </summary>
    internal sealed class SyncFactory : IDisposable
    {
        private readonly Lazy<MessageHandler> messageHandler;
        private readonly Lazy<TaskScheduler> scheduler;
        private readonly IntPtr? providedHandle;
        private bool hasUIThread;
        private bool disposed;

        internal SyncFactory(IntPtr? messagePumpHandle = null)
        {
            providedHandle = messagePumpHandle;

            scheduler = new Lazy<TaskScheduler>(() =>
            {
                var dispatcher = Dispatcher.FromThread(Thread.CurrentThread);
                if (dispatcher != null && SynchronizationContext.Current != null)
                {
                    hasUIThread = true;
                    return TaskScheduler.FromCurrentSynchronizationContext();
                }

                if (SynchronizationContext.Current is WindowsFormsSynchronizationContext)
                {
                    hasUIThread = true;
                    return TaskScheduler.FromCurrentSynchronizationContext();
                }

                TaskScheduler current = null;
                var ready = new ManualResetEventSlim(false);

                var thread = new Thread(() =>
                {
                    Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
                    {
                        current = TaskScheduler.FromCurrentSynchronizationContext();
                        ready.Set();
                    }), DispatcherPriority.Normal);
                    Dispatcher.Run();
                })
                {
                    IsBackground = true,
                    Name = "EventHook.MessagePump"
                };
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();

                if (!ready.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException("Timed out creating EventHook message pump.");
                }

                return current;
            });

            messageHandler = new Lazy<MessageHandler>(() =>
            {
                MessageHandler msgHandler = null;
                var ready = new ManualResetEventSlim(false);

                Task.Factory.StartNew(() =>
                    {
                        msgHandler = new MessageHandler();
                        ready.Set();
                    },
                    CancellationToken.None,
                    TaskCreationOptions.None,
                    GetTaskScheduler());

                if (!ready.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException("Timed out creating EventHook message window.");
                }

                return msgHandler;
            });

            GetTaskScheduler();
            if (providedHandle == null || providedHandle == IntPtr.Zero)
            {
                _ = messageHandler.Value;
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;

            try
            {
                if (messageHandler.IsValueCreated)
                {
                    Task.Factory.StartNew(() =>
                        {
                            messageHandler.Value.DestroyHandle();
                            if (!hasUIThread)
                            {
                                Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
                            }
                        },
                        CancellationToken.None,
                        TaskCreationOptions.None,
                        GetTaskScheduler()).Wait(TimeSpan.FromSeconds(5));
                }
            }
            catch
            {
                // best-effort shutdown
            }
        }

        internal TaskScheduler GetTaskScheduler() => scheduler.Value;

        internal MessageHandler GetMessageHandler()
        {
            if (providedHandle != null && providedHandle != IntPtr.Zero)
            {
                return null;
            }

            return messageHandler.Value;
        }

        internal IntPtr GetHandle()
        {
            if (providedHandle != null && providedHandle != IntPtr.Zero)
            {
                return providedHandle.Value;
            }

            // Always use the dedicated message window so hotkeys/shell hooks receive messages
            // on the same HWND we own (avoids relying on process MainWindowHandle).
            return messageHandler.Value.Handle;
        }

        internal void RunOnPump(Action action)
        {
            Task.Factory.StartNew(action, CancellationToken.None, TaskCreationOptions.None, GetTaskScheduler())
                .Wait();
        }
    }

    /// <summary>
    /// Invisible NativeWindow used as a hook / hotkey message target.
    /// </summary>
    internal class MessageHandler : NativeWindow
    {
        internal event Action<Message> MessageReceived;

        internal MessageHandler()
        {
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message msg)
        {
            MessageReceived?.Invoke(msg);
            base.WndProc(ref msg);
        }
    }

    internal static class HotkeyNative
    {
        internal const int WM_HOTKEY = 0x0312;
        internal const uint MOD_ALT = 0x0001;
        internal const uint MOD_CONTROL = 0x0002;
        internal const uint MOD_SHIFT = 0x0004;
        internal const uint MOD_WIN = 0x0008;
        internal const uint MOD_NOREPEAT = 0x4000;
    }
}
