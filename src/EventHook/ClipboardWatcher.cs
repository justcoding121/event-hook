using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using EventHook.Helpers;
using EventHook.Hooks;

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
        private ClipBoardHook clip;
        private AsyncConcurrentQueue<object> clipQueue;
        private CancellationTokenSource taskCancellationTokenSource;
        private bool isRunning;
        private bool disposed;

        internal ClipboardWatcher(SyncFactory factory)
        {
            this.factory = factory;
        }

        public event EventHandler<ClipboardEventArgs> OnClipboardModified;

        public void Start()
        {
            lock (accesslock)
            {
                if (isRunning)
                {
                    return;
                }

                taskCancellationTokenSource = new CancellationTokenSource();
                clipQueue = new AsyncConcurrentQueue<object>(taskCancellationTokenSource.Token);

                factory.RunOnPump(() =>
                {
                    clip = new ClipBoardHook();
                    clip.RegisterClipboardViewer();
                    clip.ClipBoardChanged += ClipboardHandler;
                });

                Task.Factory.StartNew(ClipConsumerAsync);
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
                    if (clip != null)
                    {
                        clip.ClipBoardChanged -= ClipboardHandler;
                        clip.UnregisterClipboardViewer();
                        clip.Dispose();
                        clip = null;
                    }
                });

                isRunning = false;
                clipQueue.Enqueue(false);
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

        private void ClipboardHandler(object sender, EventArgs e)
        {
            try
            {
                clipQueue?.Enqueue(sender);
            }
            catch
            {
                // ignore
            }
        }

        private async Task ClipConsumerAsync()
        {
            while (isRunning)
            {
                object item;
                try
                {
                    item = await clipQueue.DequeueAsync();
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

                RaiseClipboard((IDataObject)item);
            }
        }

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
    }
}
