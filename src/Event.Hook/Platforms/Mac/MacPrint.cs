using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using EventHook.Helpers;
using EventHook.Platforms.Mac.Native;

namespace EventHook.Platforms.Mac
{
    /// <summary>
    /// CUPS print job watcher: cupsGetDests + poll cupsGetJobs.
    /// </summary>
    internal sealed class MacPrint : IDisposable
    {
        private Action<PrintEventData> onJob;
        private Timer timer;
        private readonly HashSet<string> seenJobs = new HashSet<string>();
        private bool primed;
        private bool disposed;

        internal HookStartResult Start(Action<PrintEventData> enqueue)
        {
            if (enqueue == null)
            {
                throw new ArgumentNullException(nameof(enqueue));
            }

            if (timer != null)
            {
                return HookStartResult.Ok();
            }

            try
            {
                var num = MacNative.cupsGetDests(out var dests);
                if (dests != IntPtr.Zero)
                {
                    MacNative.cupsFreeDests(num, dests);
                }

                if (num < 0)
                {
                    return HookStartResult.Fail(
                        HookFailureReason.NativeFailure,
                        "cupsGetDests failed. Is the CUPS library / scheduler available?");
                }
            }
            catch (DllNotFoundException)
            {
                return HookStartResult.Fail(
                    HookFailureReason.NativeFailure,
                    "libcups was not found. Install CUPS development libraries.");
            }
            catch (Exception ex)
            {
                return HookStartResult.Fail(HookFailureReason.NativeFailure, ex.Message);
            }

            onJob = enqueue;
            timer = new Timer(OnPoll, null, TimeSpan.FromMilliseconds(750), TimeSpan.FromMilliseconds(750));
            return HookStartResult.Ok();
        }

        internal void Stop()
        {
            timer?.Dispose();
            timer = null;
            onJob = null;
            seenJobs.Clear();
            primed = false;
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

        private void OnPoll(object state)
        {
            try
            {
                var numDests = MacNative.cupsGetDests(out var dests);
                if (numDests <= 0 || dests == IntPtr.Zero)
                {
                    if (dests != IntPtr.Zero)
                    {
                        MacNative.cupsFreeDests(numDests, dests);
                    }

                    return;
                }

                var current = new HashSet<string>();
                var destSize = Marshal.SizeOf<MacNative.cups_dest_t>();
                for (var i = 0; i < numDests; i++)
                {
                    var dest = Marshal.PtrToStructure<MacNative.cups_dest_t>(dests + i * destSize);
                    var printerName = dest.name != IntPtr.Zero
                        ? Marshal.PtrToStringAnsi(dest.name)
                        : null;
                    if (string.IsNullOrEmpty(printerName))
                    {
                        continue;
                    }

                    var numJobs = MacNative.cupsGetJobs(out var jobs, printerName, 0, MacNative.CUPS_WHICHJOBS_ACTIVE);
                    if (numJobs <= 0 || jobs == IntPtr.Zero)
                    {
                        if (jobs != IntPtr.Zero)
                        {
                            MacNative.cupsFreeJobs(numJobs, jobs);
                        }

                        continue;
                    }

                    var jobSize = Marshal.SizeOf<MacNative.cups_job_t>();
                    for (var j = 0; j < numJobs; j++)
                    {
                        var job = Marshal.PtrToStructure<MacNative.cups_job_t>(jobs + j * jobSize);
                        var key = printerName + ":" + job.id;
                        current.Add(key);
                        if (primed && !seenJobs.Contains(key))
                        {
                            var title = job.title != IntPtr.Zero
                                ? Marshal.PtrToStringAnsi(job.title)
                                : string.Empty;
                            onJob?.Invoke(new PrintEventData
                            {
                                EventDateTime = DateTime.Now,
                                PrinterName = printerName,
                                JobName = title,
                                JobSize = job.size,
                                Pages = null
                            });
                        }
                    }

                    MacNative.cupsFreeJobs(numJobs, jobs);
                }

                MacNative.cupsFreeDests(numDests, dests);

                if (!primed)
                {
                    foreach (var key in current)
                    {
                        seenJobs.Add(key);
                    }

                    primed = true;
                }
                else
                {
                    seenJobs.Clear();
                    foreach (var key in current)
                    {
                        seenJobs.Add(key);
                    }
                }
            }
            catch
            {
                // never throw from timer
            }
        }
    }
}
