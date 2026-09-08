using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using EventHook.Platforms.Mac.Native;

namespace EventHook.Platforms.Mac
{
    /// <summary>
    /// Dedicated background thread running a CFRunLoop for CGEvent taps, Carbon handlers, and AX observers.
    /// </summary>
    internal sealed class MacRunLoopHost : IDisposable
    {
        private static readonly object Gate = new object();
        private static MacRunLoopHost instance;

        private readonly ConcurrentQueue<Action> work = new ConcurrentQueue<Action>();
        private readonly ManualResetEventSlim ready = new ManualResetEventSlim(false);
        private readonly Thread thread;
        private IntPtr runLoop;
        private IntPtr workSource;
        private MacNative.CFRunLoopSourcePerform performKeepAlive;
        private volatile bool stopping;
        private bool disposed;

        private MacRunLoopHost()
        {
            thread = new Thread(RunLoopThread)
            {
                IsBackground = true,
                Name = "EventHook.Mac.CFRunLoop"
            };
            thread.Start();
            if (!ready.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("Timed out starting macOS CFRunLoop host.");
            }
        }

        internal static MacRunLoopHost Shared
        {
            get
            {
                if (instance != null)
                {
                    return instance;
                }

                lock (Gate)
                {
                    return instance ??= new MacRunLoopHost();
                }
            }
        }

        internal IntPtr RunLoop => runLoop;

        internal void RunOnLoop(Action action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            if (disposed || stopping)
            {
                return;
            }

            if (Thread.CurrentThread == thread)
            {
                action();
                return;
            }

            using var done = new ManualResetEventSlim(false);
            Exception error = null;
            work.Enqueue(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    done.Set();
                }
            });

            MacNative.CFRunLoopSourceSignal(workSource);
            MacNative.CFRunLoopWakeUp(runLoop);

            if (!done.Wait(TimeSpan.FromSeconds(15)))
            {
                throw new TimeoutException("Timed out marshaling work onto the macOS CFRunLoop.");
            }

            if (error != null)
            {
                throw error;
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            stopping = true;
            try
            {
                if (runLoop != IntPtr.Zero)
                {
                    MacNative.CFRunLoopStop(runLoop);
                    MacNative.CFRunLoopWakeUp(runLoop);
                }
            }
            catch
            {
                // best-effort
            }

            try
            {
                thread.Join(TimeSpan.FromSeconds(5));
            }
            catch
            {
                // ignore
            }

            lock (Gate)
            {
                ClearSharedInstance(this);
            }
        }

        private static void ClearSharedInstance(MacRunLoopHost host)
        {
            if (ReferenceEquals(instance, host))
            {
                instance = null;
            }
        }

        private void RunLoopThread()
        {
            try
            {
                try
                {
                    MacNative.NSApplicationLoad();
                }
                catch
                {
                    // AppKit may already be loaded
                }

                runLoop = MacNative.CFRunLoopGetCurrent();
                performKeepAlive = OnWorkSourcePerform;
                var ctx = new MacNative.CFRunLoopSourceContext
                {
                    version = 0,
                    info = IntPtr.Zero,
                    perform = Marshal.GetFunctionPointerForDelegate(performKeepAlive)
                };
                workSource = MacNative.CFRunLoopSourceCreate(IntPtr.Zero, 0, ref ctx);
                MacNative.CFRunLoopAddSource(runLoop, workSource, MacNative.KCFRunLoopDefaultMode);
                MacNative.CFRunLoopAddSource(runLoop, workSource, MacNative.KCFRunLoopCommonModes);
                ready.Set();
                MacNative.CFRunLoopRun();
            }
            catch (Exception)
            {
                ready.Set();
                throw;
            }
            finally
            {
                if (workSource != IntPtr.Zero)
                {
                    try
                    {
                        MacNative.CFRunLoopRemoveSource(runLoop, workSource, MacNative.KCFRunLoopDefaultMode);
                        MacNative.CFRelease(workSource);
                    }
                    catch
                    {
                        // ignore
                    }

                    workSource = IntPtr.Zero;
                }
            }
        }

        private void OnWorkSourcePerform(IntPtr info)
        {
            _ = info;
            while (work.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch
                {
                    // never throw out of run-loop source
                }
            }
        }
    }
}
