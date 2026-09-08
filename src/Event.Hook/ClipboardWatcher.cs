using System;
using System.Threading;
using System.Threading.Tasks;
using EventHook.Helpers;

#if WINDOWS
using System.Threading.Channels;
using System.Windows.Forms;
using EventHook.Hooks;
#endif

namespace EventHook
{
    /// <summary>
    /// Type of clipboard content.
    /// </summary>
    public enum ClipboardContentTypes
    {
        PlainText = 0,
        RichText = 1,
        Html = 2,
        Csv = 3,
        UnicodeText = 4,
        Image = 5,
        FileDrop = 6,
        Other = 7
    }

    public class ClipboardEventArgs : EventArgs
    {
        public object Data { get; set; }
        public ClipboardContentTypes DataFormat { get; set; }
    }

    /// <summary>
    /// Clipboard watcher including text, images, and file drops.
    /// Context-menu paste is visible here because it changes clipboard contents globally;
    /// WM_PASTE itself is application-local and is not hooked.
    /// </summary>
    public class ClipboardWatcher : IDisposable
    {
        private readonly object accesslock = new object();
        private readonly SyncFactory factory;
        private bool isRunning;
        private bool disposed;

#if WINDOWS
        private ClipBoardHook clip;
        private EventOffload<object> offload;
        private CancellationTokenSource taskCancellationTokenSource;
#endif

        internal ClipboardWatcher(SyncFactory factory)
        {
            this.factory = factory;
        }

#pragma warning disable CS0067 // Raised only on Windows implementation
        public event EventHandler<ClipboardEventArgs> OnClipboardModified;
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
                offload = new EventOffload<object>();

                try
                {
                    factory.RunOnPump(() =>
                    {
                        clip = new ClipBoardHook();
                        clip.RegisterClipboardViewer();
                        clip.ClipBoardChanged += ClipboardHandler;
                    });
                }
                catch (Exception ex)
                {
                    CleanupFailedStart();
                    return HookStartResult.Fail(HookFailureReason.NativeFailure, ex.Message);
                }

                Task.Factory.StartNew(ClipConsumerAsync, TaskCreationOptions.LongRunning);
                isRunning = true;
                return HookStartResult.Ok();
#else
                if (OperatingSystem.IsWindows())
                {
                    return PlatformSupport.WindowsOnlyTfm();
                }

                return PlatformSupport.NotSupportedYet("Clipboard", PlatformSupport.CurrentOsName);
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
                    if (clip != null)
                    {
                        clip.ClipBoardChanged -= ClipboardHandler;
                        clip.UnregisterClipboardViewer();
                        clip.Dispose();
                        clip = null;
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
        private void CleanupFailedStart()
        {
            if (clip != null)
            {
                try
                {
                    factory.RunOnPump(() =>
                    {
                        clip.ClipBoardChanged -= ClipboardHandler;
                        clip.UnregisterClipboardViewer();
                        clip.Dispose();
                        clip = null;
                    });
                }
                catch
                {
                    clip = null;
                }
            }

            offload?.Dispose();
            offload = null;
            taskCancellationTokenSource?.Dispose();
            taskCancellationTokenSource = null;
        }

        private void ClipboardHandler(object sender, EventArgs e)
        {
            try
            {
                // Enqueue the data object reference only; classify on the consumer.
                offload?.TryWrite(sender);
            }
            catch
            {
                // ignore
            }
        }

        private async Task ClipConsumerAsync()
        {
            var token = taskCancellationTokenSource.Token;
            while (!token.IsCancellationRequested)
            {
                object item;
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

                if (item is IDataObject data)
                {
                    RaiseClipboard(data);
                }
            }
        }

        /// <summary>
        /// Classify clipboard content. Windows-only (WinForms data formats).
        /// </summary>
        internal static bool TryClassify(IDataObject iData, out ClipboardContentTypes format, out object data)
        {
            format = ClipboardContentTypes.Other;
            data = null;

            try
            {
                if (iData.GetDataPresent(DataFormats.FileDrop))
                {
                    format = ClipboardContentTypes.FileDrop;
                    data = iData.GetData(DataFormats.FileDrop);
                    return true;
                }

                if (iData.GetDataPresent(DataFormats.Bitmap))
                {
                    format = ClipboardContentTypes.Image;
                    data = iData.GetData(DataFormats.Bitmap);
                    return true;
                }

                if (iData.GetDataPresent(DataFormats.UnicodeText))
                {
                    format = ClipboardContentTypes.UnicodeText;
                    data = iData.GetData(DataFormats.UnicodeText);
                    return true;
                }

                if (iData.GetDataPresent(DataFormats.Text))
                {
                    format = ClipboardContentTypes.PlainText;
                    data = iData.GetData(DataFormats.Text);
                    return true;
                }

                if (iData.GetDataPresent(DataFormats.Rtf))
                {
                    format = ClipboardContentTypes.RichText;
                    data = iData.GetData(DataFormats.Rtf);
                    return true;
                }

                if (iData.GetDataPresent(DataFormats.CommaSeparatedValue))
                {
                    format = ClipboardContentTypes.Csv;
                    data = iData.GetData(DataFormats.CommaSeparatedValue);
                    return true;
                }

                if (iData.GetDataPresent(DataFormats.Html))
                {
                    format = ClipboardContentTypes.Html;
                    data = iData.GetData(DataFormats.Html);
                    return true;
                }

                if (iData.GetDataPresent(DataFormats.StringFormat))
                {
                    format = ClipboardContentTypes.PlainText;
                    data = iData.GetData(DataFormats.StringFormat);
                    return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private void RaiseClipboard(IDataObject iData)
        {
            if (!TryClassify(iData, out var format, out var data))
            {
                return;
            }

            try
            {
                OnClipboardModified?.Invoke(this, new ClipboardEventArgs { Data = data, DataFormat = format });
            }
            catch
            {
                // swallow
            }
        }
#endif
    }
}
