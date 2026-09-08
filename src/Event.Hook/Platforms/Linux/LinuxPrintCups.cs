using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using EventHook.Helpers;

namespace EventHook.Platforms.Linux
{
    /// <summary>
    /// CUPS print-job polling via <c>cupsGetDests</c> + <c>cupsGetJobs</c>.
    /// </summary>
    internal sealed class LinuxPrintCups : IDisposable
    {
        private readonly Action<PrintEventData> onJob;
        private Thread thread;
        private volatile bool running;
        private bool disposed;
        private readonly HashSet<string> seenJobs = new HashSet<string>();

        internal LinuxPrintCups(Action<PrintEventData> onJob)
        {
            this.onJob = onJob;
        }

        internal HookStartResult Start()
        {
            if (running)
            {
                return HookStartResult.Ok();
            }

            try
            {
                var destCount = 0;
                IntPtr dests = IntPtr.Zero;
                destCount = CupsGetDests(ref dests);
                if (dests != IntPtr.Zero)
                {
                    CupsFreeDests(destCount, dests);
                }

                // Zero destinations is still a successful start (empty printer list).
            }
            catch (DllNotFoundException ex)
            {
                return HookStartResult.Fail(HookFailureReason.NativeFailure, "Missing CUPS library: " + ex.Message);
            }
            catch (Exception ex)
            {
                return HookStartResult.Fail(HookFailureReason.NativeFailure, ex.Message);
            }

            running = true;
            thread = new Thread(PollLoop)
            {
                IsBackground = true,
                Name = "EventHook.Linux.CUPS"
            };
            thread.Start();
            return HookStartResult.Ok();
        }

        internal void Stop()
        {
            running = false;
            if (thread != null && thread.IsAlive)
            {
                thread.Join(TimeSpan.FromSeconds(3));
            }

            thread = null;
        }

        private void PollLoop()
        {
            while (running)
            {
                try
                {
                    PollOnce();
                }
                catch
                {
                    // keep polling
                }

                Thread.Sleep(1000);
            }
        }

        private void PollOnce()
        {
            IntPtr dests = IntPtr.Zero;
            var destCount = CupsGetDests(ref dests);
            if (destCount <= 0 || dests == IntPtr.Zero)
            {
                if (dests != IntPtr.Zero)
                {
                    CupsFreeDests(destCount, dests);
                }

                return;
            }

            try
            {
                var stride = Marshal.SizeOf<CupsDest>();
                for (var i = 0; i < destCount; i++)
                {
                    var dest = Marshal.PtrToStructure<CupsDest>(IntPtr.Add(dests, i * stride));
                    if (dest.name == IntPtr.Zero)
                    {
                        continue;
                    }

                    var printerName = Marshal.PtrToStringAnsi(dest.name);
                    PollJobs(printerName);
                }
            }
            finally
            {
                CupsFreeDests(destCount, dests);
            }
        }

        private void PollJobs(string printerName)
        {
            IntPtr jobs = IntPtr.Zero;
            var count = CupsGetJobs(ref jobs, printerName, 0, CupsWhichJobsAll);
            if (count <= 0 || jobs == IntPtr.Zero)
            {
                if (jobs != IntPtr.Zero)
                {
                    CupsFreeJobs(count, jobs);
                }

                return;
            }

            try
            {
                var stride = Marshal.SizeOf<CupsJob>();
                for (var i = 0; i < count; i++)
                {
                    var job = Marshal.PtrToStructure<CupsJob>(IntPtr.Add(jobs, i * stride));
                    var key = printerName + ":" + job.id;
                    if (!seenJobs.Add(key))
                    {
                        continue;
                    }

                    // Emit when job is processing / held after creation (approx spooling).
                    if (job.state != IppJobPending && job.state != IppJobProcessing && job.state != IppJobHeld)
                    {
                        continue;
                    }

                    var title = job.title != IntPtr.Zero ? Marshal.PtrToStringAnsi(job.title) : string.Empty;
                    var data = new PrintEventData
                    {
                        EventDateTime = DateTime.Now,
                        PrinterName = printerName,
                        JobName = title,
                        Pages = job.size > 0 ? (int?)null : null,
                        JobSize = job.size > 0 ? job.size : (int?)null
                    };

                    onJob?.Invoke(data);
                }
            }
            finally
            {
                CupsFreeJobs(count, jobs);
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

        private const int CupsWhichJobsAll = -1;
        private const int IppJobPending = 3;
        private const int IppJobHeld = 4;
        private const int IppJobProcessing = 5;

        [StructLayout(LayoutKind.Sequential)]
        private struct CupsDest
        {
            public IntPtr name;
            public IntPtr instance;
            public int isDefault;
            public int numOptions;
            public IntPtr options;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CupsJob
        {
            public int id;
            public IntPtr dest;
            public IntPtr title;
            public IntPtr user;
            public IntPtr format;
            public int state; // ipp_jstate_t
            public int size;
            public int priority;
            public long completedTime;
            public long creationTime;
            public long processingTime;
        }

        [DllImport("libcups.so.2", CallingConvention = CallingConvention.Cdecl, EntryPoint = "cupsGetDests")]
        private static extern int CupsGetDests(ref IntPtr dests);

        [DllImport("libcups.so.2", CallingConvention = CallingConvention.Cdecl, EntryPoint = "cupsFreeDests")]
        private static extern void CupsFreeDests(int numDests, IntPtr dests);

        [DllImport("libcups.so.2", CallingConvention = CallingConvention.Cdecl, EntryPoint = "cupsGetJobs")]
        private static extern int CupsGetJobs(ref IntPtr jobs, string name, int myJobs, int whichJobs);

        [DllImport("libcups.so.2", CallingConvention = CallingConvention.Cdecl, EntryPoint = "cupsFreeJobs")]
        private static extern void CupsFreeJobs(int numJobs, IntPtr jobs);
    }
}
