using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using EventHook.Helpers;

namespace EventHook.Platforms.Linux
{
    /// <summary>
    /// Owns one X11 <c>Display*</c> on a dedicated thread. Xlib is not thread-safe —
    /// all X protocol calls for this connection must go through <see cref="Invoke"/> / the event loop.
    /// </summary>
    internal sealed class LinuxX11Display : IDisposable
    {
        private readonly ConcurrentQueue<Action> work = new ConcurrentQueue<Action>();
        private readonly AutoResetEvent workSignal = new AutoResetEvent(false);
        private readonly List<Action<LinuxX11Native.XEvent>> handlers = new List<Action<LinuxX11Native.XEvent>>();
        private readonly object handlersLock = new object();
        private Thread thread;
        private IntPtr display;
        private IntPtr root;
        private int screen;
        private volatile bool running;
        private volatile bool disposed;
        private Exception startError;
        private readonly ManualResetEventSlim ready = new ManualResetEventSlim(false);

        internal IntPtr Display => display;
        internal IntPtr Root => root;
        internal int Screen => screen;
        internal bool IsRunning => running && display != IntPtr.Zero;

        internal static HookStartResult TryOpen(out LinuxX11Display instance)
        {
            instance = null;
            var gate = LinuxSession.RequireX11("X11");
            if (!gate.Success)
            {
                return gate;
            }

            LinuxSession.EnsureXInitThreads();

            try
            {
                instance = new LinuxX11Display();
                instance.StartThread();
                if (!instance.ready.Wait(TimeSpan.FromSeconds(10)))
                {
                    instance.Dispose();
                    instance = null;
                    return HookStartResult.Fail(HookFailureReason.NativeFailure, "Timed out opening X11 display.");
                }

                if (instance.startError != null)
                {
                    var message = instance.startError.Message;
                    instance.Dispose();
                    instance = null;
                    return HookStartResult.Fail(HookFailureReason.NativeFailure, message);
                }

                if (instance.display == IntPtr.Zero)
                {
                    instance.Dispose();
                    instance = null;
                    return HookStartResult.Fail(
                        HookFailureReason.DisplayUnavailable,
                        "XOpenDisplay failed for DISPLAY=" + (LinuxSession.Display ?? "(null)"));
                }

                return HookStartResult.Ok();
            }
            catch (DllNotFoundException ex)
            {
                instance?.Dispose();
                instance = null;
                return HookStartResult.Fail(HookFailureReason.NativeFailure, "Missing X11 library: " + ex.Message);
            }
            catch (Exception ex)
            {
                instance?.Dispose();
                instance = null;
                return HookStartResult.Fail(HookFailureReason.NativeFailure, ex.Message);
            }
        }

        internal void AddHandler(Action<LinuxX11Native.XEvent> handler)
        {
            if (handler == null)
            {
                return;
            }

            lock (handlersLock)
            {
                handlers.Add(handler);
            }
        }

        internal void RemoveHandler(Action<LinuxX11Native.XEvent> handler)
        {
            lock (handlersLock)
            {
                handlers.Remove(handler);
            }
        }

        /// <summary>
        /// Run <paramref name="action"/> on the X thread and wait. Startup/teardown only — never from an input callback.
        /// </summary>
        internal void Invoke(Action action)
        {
            if (action == null || disposed)
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
            workSignal.Set();
            if (!done.Wait(TimeSpan.FromSeconds(15)))
            {
                throw new TimeoutException("Timed out waiting for X11 thread.");
            }

            if (error != null)
            {
                throw error;
            }
        }

        internal void Post(Action action)
        {
            if (action == null || disposed)
            {
                return;
            }

            work.Enqueue(action);
            workSignal.Set();
        }

        private void StartThread()
        {
            running = true;
            thread = new Thread(ThreadMain)
            {
                IsBackground = true,
                Name = "EventHook.Linux.X11"
            };
            thread.Start();
        }

        private void ThreadMain()
        {
            try
            {
                display = LinuxX11Native.XOpenDisplay(null);
                if (display == IntPtr.Zero)
                {
                    startError = new InvalidOperationException("XOpenDisplay returned null.");
                    return;
                }

                screen = LinuxX11Native.XDefaultScreen(display);
                root = LinuxX11Native.XRootWindow(display, screen);
            }
            catch (Exception ex)
            {
                startError = ex;
            }
            finally
            {
                ready.Set();
            }

            if (display == IntPtr.Zero)
            {
                return;
            }

            while (running)
            {
                DrainWork();

                while (LinuxX11Native.XPending(display) != 0)
                {
                    if (LinuxX11Native.XNextEvent(display, out var ev) != 0)
                    {
                        // XNextEvent returns 0 normally; non-zero is unusual — continue.
                    }

                    Action<LinuxX11Native.XEvent>[] snapshot;
                    lock (handlersLock)
                    {
                        snapshot = handlers.ToArray();
                    }

                    foreach (var handler in snapshot)
                    {
                        try
                        {
                            handler(ev);
                        }
                        catch
                        {
                            // never throw from X dispatch
                        }
                    }
                }

                workSignal.WaitOne(25);
            }

            DrainWork();

            try
            {
                LinuxX11Native.XCloseDisplay(display);
            }
            catch
            {
                // best-effort
            }

            display = IntPtr.Zero;
        }

        private void DrainWork()
        {
            while (work.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch
                {
                    // swallow work-item errors
                }
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            running = false;
            workSignal.Set();
            if (thread != null && thread.IsAlive && Thread.CurrentThread != thread)
            {
                thread.Join(TimeSpan.FromSeconds(5));
            }

            workSignal.Dispose();
            ready.Dispose();
        }
    }
}
