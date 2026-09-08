using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using EventHook.Helpers;
using EventHook.Hooks;

namespace EventHook.Platforms.Linux
{
    /// <summary>
    /// EWMH window events: active window, title (<c>_NET_WM_NAME</c>), minimize (<c>_NET_WM_STATE_HIDDEN</c>).
    /// X handlers only enqueue (hwnd, kind); public events fire on a consumer task.
    /// </summary>
    internal sealed class LinuxWindowHookExBackend : IDisposable
    {
        private const int KindActivated = 0;
        private const int KindMinimized = 1;
        private const int KindUnminimized = 2;
        private const int KindTextChanged = 3;

        private readonly object gate = new object();
        private LinuxX11Display display;
        private Action<LinuxX11Native.XEvent> handler;
        private EventOffload<(IntPtr Hwnd, int Kind)> offload;
        private CancellationTokenSource cts;
        private IntPtr netActiveWindow;
        private IntPtr netWmName;
        private IntPtr netWmState;
        private IntPtr netWmStateHidden;
        private IntPtr lastActive = IntPtr.Zero;
        private readonly HashSet<IntPtr> minimizedWindows = new HashSet<IntPtr>();
        private bool isRunning;
        private bool disposed;

        private EventHandler<WindowEventArgs> activated;
        private EventHandler<WindowEventArgs> minimizedChanged;
        private EventHandler<WindowEventArgs> unminimized;
        private EventHandler<WindowEventArgs> textChanged;

        internal event EventHandler<WindowEventArgs> Activated
        {
            add { lock (gate) { activated += value; } }
            remove { lock (gate) { activated -= value; } }
        }

        internal event EventHandler<WindowEventArgs> Minimized
        {
            add { lock (gate) { minimizedChanged += value; } }
            remove { lock (gate) { minimizedChanged -= value; } }
        }

        internal event EventHandler<WindowEventArgs> Unminimized
        {
            add { lock (gate) { unminimized += value; } }
            remove { lock (gate) { unminimized -= value; } }
        }

        internal event EventHandler<WindowEventArgs> TextChanged
        {
            add { lock (gate) { textChanged += value; } }
            remove { lock (gate) { textChanged -= value; } }
        }

        internal bool IsRunning
        {
            get
            {
                lock (gate)
                {
                    return isRunning;
                }
            }
        }

        internal HookStartResult Start()
        {
            lock (gate)
            {
                if (isRunning)
                {
                    return HookStartResult.Ok();
                }

                var gateResult = LinuxSession.RequireX11("WindowHookEx");
                if (!gateResult.Success)
                {
                    return gateResult;
                }

                var open = LinuxX11Display.TryOpen(out display);
                if (!open.Success)
                {
                    return open;
                }

                try
                {
                    Exception error = null;
                    display.Invoke(() =>
                    {
                        try
                        {
                            netActiveWindow = LinuxX11Native.XInternAtom(display.Display, "_NET_ACTIVE_WINDOW", 0);
                            netWmName = LinuxX11Native.XInternAtom(display.Display, "_NET_WM_NAME", 0);
                            netWmState = LinuxX11Native.XInternAtom(display.Display, "_NET_WM_STATE", 0);
                            netWmStateHidden = LinuxX11Native.XInternAtom(display.Display, "_NET_WM_STATE_HIDDEN", 0);

                            LinuxX11Native.XSelectInput(
                                display.Display,
                                display.Root,
                                LinuxX11Native.PropertyChangeMask |
                                LinuxX11Native.SubstructureNotifyMask |
                                LinuxX11Native.StructureNotifyMask);

                            LinuxX11Native.XFlush(display.Display);
                        }
                        catch (Exception ex)
                        {
                            error = ex;
                        }
                    });

                    if (error != null)
                    {
                        display.Dispose();
                        display = null;
                        return HookStartResult.Fail(HookFailureReason.NativeFailure, error.Message);
                    }

                    offload = new EventOffload<(IntPtr, int)>();
                    cts = new CancellationTokenSource();
                    handler = OnXEvent;
                    display.AddHandler(handler);
                    Task.Factory.StartNew(ConsumeAsync, TaskCreationOptions.LongRunning);
                    isRunning = true;
                    return HookStartResult.Ok();
                }
                catch (Exception ex)
                {
                    display?.Dispose();
                    display = null;
                    return HookStartResult.Fail(HookFailureReason.NativeFailure, ex.Message);
                }
            }
        }

        internal void Stop()
        {
            lock (gate)
            {
                if (display != null && handler != null)
                {
                    display.RemoveHandler(handler);
                }

                display?.Dispose();
                display = null;
                handler = null;
                offload?.Complete();
                cts?.Cancel();
                cts?.Dispose();
                cts = null;
                offload?.Dispose();
                offload = null;
                isRunning = false;
                minimizedWindows.Clear();
                lastActive = IntPtr.Zero;
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
            activated = minimizedChanged = unminimized = textChanged = null;
        }

        private void OnXEvent(LinuxX11Native.XEvent ev)
        {
            try
            {
                switch (ev.type)
                {
                    case LinuxX11Native.PropertyNotify:
                    {
                        var prop = LinuxX11Native.EventAs<LinuxX11Native.XPropertyEvent>(ref ev);
                        if (prop.window == display.Root && prop.atom == netActiveWindow)
                        {
                            var active = ReadActiveWindow();
                            if (active != IntPtr.Zero && active != lastActive)
                            {
                                lastActive = active;
                                SelectWindowProps(active);
                                offload?.TryWrite((active, KindActivated));
                            }

                            break;
                        }

                        if (prop.atom == netWmName && prop.window != IntPtr.Zero)
                        {
                            offload?.TryWrite((prop.window, KindTextChanged));
                            break;
                        }

                        if (prop.atom == netWmState && prop.window != IntPtr.Zero)
                        {
                            var isHidden = WindowHasHiddenState(prop.window);
                            if (isHidden)
                            {
                                if (minimizedWindows.Add(prop.window))
                                {
                                    offload?.TryWrite((prop.window, KindMinimized));
                                }
                            }
                            else if (minimizedWindows.Remove(prop.window))
                            {
                                offload?.TryWrite((prop.window, KindUnminimized));
                            }
                        }

                        break;
                    }
                    case LinuxX11Native.CreateNotify:
                    {
                        var created = LinuxX11Native.EventAs<LinuxX11Native.XCreateWindowEvent>(ref ev);
                        if (created.window != IntPtr.Zero && created.parent == display.Root)
                        {
                            SelectWindowProps(created.window);
                        }

                        break;
                    }
                    case LinuxX11Native.DestroyNotify:
                    {
                        var destroyed = LinuxX11Native.EventAs<LinuxX11Native.XDestroyWindowEvent>(ref ev);
                        minimizedWindows.Remove(destroyed.window);
                        if (destroyed.window == lastActive)
                        {
                            lastActive = IntPtr.Zero;
                        }

                        break;
                    }
                }
            }
            catch
            {
                // never throw from X handler
            }
        }

        private async Task ConsumeAsync()
        {
            var token = cts.Token;
            while (!token.IsCancellationRequested)
            {
                (IntPtr Hwnd, int Kind) item;
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

                try
                {
                    var args = new WindowEventArgs(item.Hwnd);
                    switch (item.Kind)
                    {
                        case KindActivated:
                            activated?.Invoke(this, args);
                            break;
                        case KindMinimized:
                            minimizedChanged?.Invoke(this, args);
                            break;
                        case KindUnminimized:
                            unminimized?.Invoke(this, args);
                            break;
                        case KindTextChanged:
                            textChanged?.Invoke(this, args);
                            break;
                    }
                }
                catch
                {
                    // swallow subscriber exceptions
                }
            }
        }

        private void SelectWindowProps(IntPtr window)
        {
            try
            {
                LinuxX11Native.XSelectInput(display.Display, window, LinuxX11Native.PropertyChangeMask);
            }
            catch
            {
                // ignore
            }
        }

        private IntPtr ReadActiveWindow()
        {
            IntPtr actualType;
            int actualFormat;
            ulong nItems;
            ulong bytesAfter;
            IntPtr prop;
            var status = LinuxX11Native.XGetWindowProperty(
                display.Display,
                display.Root,
                netActiveWindow,
                0,
                1,
                0,
                IntPtr.Zero,
                out actualType,
                out actualFormat,
                out nItems,
                out bytesAfter,
                out prop);

            if (status != 0 || prop == IntPtr.Zero || nItems == 0)
            {
                if (prop != IntPtr.Zero)
                {
                    LinuxX11Native.XFree(prop);
                }

                return IntPtr.Zero;
            }

            var hwnd = Marshal.ReadIntPtr(prop);
            LinuxX11Native.XFree(prop);
            return hwnd;
        }

        private bool WindowHasHiddenState(IntPtr window)
        {
            IntPtr actualType;
            int actualFormat;
            ulong nItems;
            ulong bytesAfter;
            IntPtr prop;
            var status = LinuxX11Native.XGetWindowProperty(
                display.Display,
                window,
                netWmState,
                0,
                64,
                0,
                IntPtr.Zero,
                out actualType,
                out actualFormat,
                out nItems,
                out bytesAfter,
                out prop);

            if (status != 0 || prop == IntPtr.Zero || nItems == 0)
            {
                if (prop != IntPtr.Zero)
                {
                    LinuxX11Native.XFree(prop);
                }

                return false;
            }

            var found = false;
            for (ulong i = 0; i < nItems; i++)
            {
                var atom = Marshal.ReadIntPtr(prop, (int)i * IntPtr.Size);
                if (atom == netWmStateHidden)
                {
                    found = true;
                    break;
                }
            }

            LinuxX11Native.XFree(prop);
            return found;
        }
    }
}
