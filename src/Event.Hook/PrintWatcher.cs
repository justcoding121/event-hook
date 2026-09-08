using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventHook.Helpers;

#if WINDOWS
using System.Printing;
using System.Threading.Channels;
using EventHook.Hooks;
using EventHook.Hooks.Library;
#endif

namespace EventHook
{
    public class PrintEventData
    {
        public DateTime EventDateTime { get; set; }
        public string PrinterName { get; set; }
        public string JobName { get; set; }
        public int? Pages { get; set; }
        public int? JobSize { get; set; }
    }

    public class PrintEventArgs : EventArgs
    {
        public PrintEventData EventData { get; set; }
    }

    /// <summary>
    /// Watches local print queues including virtual printers such as Microsoft Print to PDF.
    /// </summary>
    public class PrintWatcher : IDisposable
    {
        private readonly object accesslock = new object();
        private readonly SyncFactory factory;
        private bool isRunning;
        private bool disposed;

#if WINDOWS
        private List<PrintQueueHook> printers;
        private EventOffload<PrintEventData> offload;
        private CancellationTokenSource taskCancellationTokenSource;
#endif

        internal PrintWatcher(SyncFactory factory)
        {
            this.factory = factory;
        }

#pragma warning disable CS0067 // Raised only on Windows implementation
        public event EventHandler<PrintEventArgs> OnPrintEvent;
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
                offload = new EventOffload<PrintEventData>();

                var startedCount = 0;
                var enumeratedEmpty = false;
                Exception failure = null;

                factory.RunOnPump(() =>
                {
                    try
                    {
                        printers = new List<PrintQueueHook>();
                        using var printServer = new PrintServer();
                        var queues = printServer.GetPrintQueues(new[]
                        {
                            EnumeratedPrintQueueTypes.Local,
                            EnumeratedPrintQueueTypes.Connections
                        });

                        var anyQueue = false;
                        foreach (var pq in queues)
                        {
                            anyQueue = true;
                            try
                            {
                                var pqm = new PrintQueueHook(pq.Name);
                                pqm.OnJobStatusChange += OnJobStatusChange;
                                pqm.Start();
                                printers.Add(pqm);
                                startedCount++;
                            }
                            catch
                            {
                                // skip queues that cannot be opened
                            }
                        }

                        enumeratedEmpty = !anyQueue;
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                    }
                });

                if (failure != null)
                {
                    CleanupFailedStart();
                    return HookStartResult.Fail(HookFailureReason.NativeFailure, failure.Message);
                }

                if (startedCount == 0 && !enumeratedEmpty)
                {
                    CleanupFailedStart();
                    return HookStartResult.Fail(
                        HookFailureReason.NativeFailure,
                        "No print queues could be opened for monitoring.");
                }

                Task.Factory.StartNew(PrintConsumerAsync, TaskCreationOptions.LongRunning);
                isRunning = true;
                return HookStartResult.Ok();
#else
                if (OperatingSystem.IsWindows())
                {
                    return PlatformSupport.WindowsOnlyTfm();
                }

                return PlatformSupport.NotSupportedYet("Print", PlatformSupport.CurrentOsName);
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
                    if (printers != null)
                    {
                        foreach (var pqm in printers)
                        {
                            pqm.OnJobStatusChange -= OnJobStatusChange;
                            try
                            {
                                pqm.Stop();
                            }
                            catch
                            {
                                // ignored
                            }
                        }

                        printers.Clear();
                    }

                    printers = null;
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
            try
            {
                factory.RunOnPump(() =>
                {
                    if (printers != null)
                    {
                        foreach (var pqm in printers)
                        {
                            pqm.OnJobStatusChange -= OnJobStatusChange;
                            try
                            {
                                pqm.Stop();
                            }
                            catch
                            {
                                // ignored
                            }
                        }

                        printers.Clear();
                    }

                    printers = null;
                });
            }
            catch
            {
                printers = null;
            }

            offload?.Dispose();
            offload = null;
            taskCancellationTokenSource?.Dispose();
            taskCancellationTokenSource = null;
        }

        private void OnJobStatusChange(object sender, PrintJobChangeEventArgs e)
        {
            try
            {
                if ((e.JobStatus & JOBSTATUS.JOB_STATUS_SPOOLING) != JOBSTATUS.JOB_STATUS_SPOOLING ||
                    e.JobInfo == null)
                {
                    return;
                }

                var printEvent = new PrintEventData
                {
                    JobName = e.JobInfo.JobName,
                    JobSize = e.JobInfo.JobSize,
                    EventDateTime = DateTime.Now,
                    Pages = e.JobInfo.NumberOfPages,
                    PrinterName = ((PrintQueueHook)sender).SpoolerName
                };

                offload?.TryWrite(printEvent);
            }
            catch
            {
                // never throw from spool callback
            }
        }

        private async Task PrintConsumerAsync()
        {
            var token = taskCancellationTokenSource.Token;
            while (!token.IsCancellationRequested)
            {
                PrintEventData printEvent;
                try
                {
                    printEvent = await offload.ReadAsync(token).ConfigureAwait(false);
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
                    OnPrintEvent?.Invoke(this, new PrintEventArgs { EventData = printEvent });
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
