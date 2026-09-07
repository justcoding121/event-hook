using System;
using System.Collections.Generic;
using System.Printing;
using System.Threading;
using System.Threading.Tasks;
using EventHook.Helpers;
using EventHook.Hooks;
using EventHook.Hooks.Library;

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
        private List<PrintQueueHook> printers;
        private bool isRunning;
        private bool disposed;

        internal PrintWatcher(SyncFactory factory)
        {
            this.factory = factory;
        }

        public event EventHandler<PrintEventArgs> OnPrintEvent;

        public void Start()
        {
            lock (accesslock)
            {
                if (isRunning)
                {
                    return;
                }

                factory.RunOnPump(() =>
                {
                    printers = new List<PrintQueueHook>();
                    using var printServer = new PrintServer();
                    foreach (var pq in printServer.GetPrintQueues(new[]
                             {
                                 EnumeratedPrintQueueTypes.Local,
                                 EnumeratedPrintQueueTypes.Connections
                             }))
                    {
                        try
                        {
                            var pqm = new PrintQueueHook(pq.Name);
                            pqm.OnJobStatusChange += OnJobStatusChange;
                            pqm.Start();
                            printers.Add(pqm);
                        }
                        catch
                        {
                            // skip queues that cannot be opened
                        }
                    }

                    isRunning = true;
                });
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
                    isRunning = false;
                });
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

                Task.Run(() =>
                {
                    try
                    {
                        OnPrintEvent?.Invoke(this, new PrintEventArgs { EventData = printEvent });
                    }
                    catch
                    {
                        // swallow
                    }
                });
            }
            catch
            {
                // never throw from spool callback
            }
        }
    }
}
