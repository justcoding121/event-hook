#if !WINDOWS
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace EventHook.E2ETests
{
    /// <summary>
    /// Temporary IPP Everywhere printer so print-job delivery can be proven without a hardware queue.
    /// </summary>
    internal sealed class MacCupsPrinter : IDisposable
    {
        private Process server;
        private bool createdQueue;
        private bool disposed;

        internal string Name { get; } = "EventHookE2E";

        internal bool TryStart()
        {
            try
            {
                var spool = Path.Combine(Path.GetTempPath(), "eventhook-e2e-print");
                Directory.CreateDirectory(spool);

                server = Process.Start(new ProcessStartInfo
                {
                    FileName = "ippeveprinter",
                    Arguments = $"-p 18631 -d \"{spool}\" -k {Name}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });
                if (server == null)
                {
                    return false;
                }

                Thread.Sleep(700);
                var add = Run("lpadmin", $"-p {Name} -E -v ipp://127.0.0.1:18631/ipp/print -m everywhere");
                createdQueue = add == 0;
                return createdQueue;
            }
            catch
            {
                return false;
            }
        }

        internal bool TryPrint(string jobTitle, string body)
        {
            try
            {
                var file = Path.Combine(Path.GetTempPath(), "eventhook-e2e-job.txt");
                File.WriteAllText(file, body);
                return Run("lp", $"-d {Name} -t {jobTitle} {file}") == 0;
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (createdQueue)
            {
                Run("cancel", $"-a {Name}");
                Run("lpadmin", $"-x {Name}");
            }

            try
            {
                if (server != null && !server.HasExited)
                {
                    server.Kill();
                    server.WaitForExit(2000);
                }
            }
            catch
            {
                // ignore
            }

            server?.Dispose();
        }

        private static int Run(string fileName, string arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            if (proc == null)
            {
                return -1;
            }

            if (!proc.WaitForExit(8000))
            {
                try
                {
                    proc.Kill(entireProcessTree: true);
                }
                catch
                {
                    // best-effort
                }

                return -1;
            }

            return proc.ExitCode;
        }
    }
}
#endif
