using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using EventHook.Helpers;
using EventHook.Hooks;
using EventHook.Platforms.Mac.Native;

namespace EventHook.Platforms.Mac
{
    /// <summary>
    /// AXObserver-based window events (title / minimize / foreground). Requires Accessibility TCC.
    /// </summary>
    internal sealed class MacWindowHookExBackend : IDisposable
    {
        private readonly object gate = new object();
        private EventOffload<WindowEventItem> offload;
        private CancellationTokenSource cts;
        private MacNative.AXObserverCallback observerCallback;
        private IntPtr observer;
        private IntPtr systemWide;
        private IntPtr notifFocused;
        private IntPtr notifTitle;
        private IntPtr notifValue;
        private IntPtr attrTitle;
        private bool isRunning;
        private bool disposed;

        private EventHandler<WindowEventArgs> activated;
        private EventHandler<WindowEventArgs> minimized;
        private EventHandler<WindowEventArgs> unminimized;
        private EventHandler<WindowEventArgs> textChanged;

        internal event EventHandler<WindowEventArgs> Activated
        {
            add { lock (gate) { activated += value; } }
            remove { lock (gate) { activated -= value; } }
        }

        internal event EventHandler<WindowEventArgs> Minimized
        {
            add { lock (gate) { minimized += value; } }
            remove { lock (gate) { minimized -= value; } }
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

                try
                {
                    if (!MacNative.AXIsProcessTrusted())
                    {
                        return PlatformSupport.MacPermission("Accessibility");
                    }
                }
                catch (Exception ex)
                {
                    return HookStartResult.Fail(HookFailureReason.NativeFailure, ex.Message);
                }

                HookStartResult result = HookStartResult.Ok();
                try
                {
                    MacRunLoopHost.Shared.RunOnLoop(() =>
                    {
                        notifFocused = MacNative.CFStringCreateWithCString(IntPtr.Zero, "AXFocusedUIElementChanged", 0x08000100);
                        notifTitle = MacNative.CFStringCreateWithCString(IntPtr.Zero, "AXTitleChanged", 0x08000100);
                        notifValue = MacNative.CFStringCreateWithCString(IntPtr.Zero, "AXValueChanged", 0x08000100);
                        attrTitle = MacNative.CFStringCreateWithCString(IntPtr.Zero, "AXTitle", 0x08000100);

                        observerCallback = OnAxNotification;
                        var pid = Environment.ProcessId;
                        // Observe system-wide via focused element changes on our process observer is limited;
                        // create observer for pid 0 is invalid — use current process and system-wide element notifications where possible.
                        var status = MacNative.AXObserverCreate(pid, observerCallback, out observer);
                        if (status != 0 || observer == IntPtr.Zero)
                        {
                            result = PlatformSupport.MacPermission("Accessibility");
                            return;
                        }

                        systemWide = MacNative.AXUIElementCreateSystemWide();
                        MacNative.AXObserverAddNotification(observer, systemWide, notifFocused, IntPtr.Zero);
                        MacNative.AXObserverAddNotification(observer, systemWide, notifTitle, IntPtr.Zero);

                        var source = MacNative.AXObserverGetRunLoopSource(observer);
                        MacNative.CFRunLoopAddSource(
                            MacRunLoopHost.Shared.RunLoop,
                            source,
                            MacNative.KCFRunLoopCommonModes);
                    });
                }
                catch (Exception ex)
                {
                    TeardownNative();
                    return HookStartResult.Fail(HookFailureReason.NativeFailure, ex.Message);
                }

                if (!result.Success)
                {
                    TeardownNative();
                    return result;
                }

                cts = new CancellationTokenSource();
                offload = new EventOffload<WindowEventItem>();
                Task.Factory.StartNew(ConsumeAsync, TaskCreationOptions.LongRunning);
                isRunning = true;
                return HookStartResult.Ok();
            }
        }

        internal void Stop()
        {
            lock (gate)
            {
                if (!isRunning)
                {
                    return;
                }

                isRunning = false;
                TeardownNative();
                offload?.Complete();
                cts?.Cancel();
                cts?.Dispose();
                cts = null;
                offload?.Dispose();
                offload = null;
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
            activated = minimized = unminimized = textChanged = null;
        }

        private void OnAxNotification(IntPtr obs, IntPtr element, IntPtr notification, IntPtr refcon)
        {
            _ = obs;
            _ = refcon;
            try
            {
                WindowEventKind kind;
                if (notification == notifFocused)
                {
                    kind = WindowEventKind.Activated;
                }
                else if (notification == notifTitle)
                {
                    kind = WindowEventKind.TextChanged;
                }
                else if (notification == notifValue)
                {
                    kind = WindowEventKind.Minimized;
                }
                else
                {
                    return;
                }

                offload?.TryWrite(new WindowEventItem(kind, element));
            }
            catch
            {
                // never throw from AX callback
            }
        }

        private async Task ConsumeAsync()
        {
            var token = cts.Token;
            while (!token.IsCancellationRequested)
            {
                WindowEventItem item;
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

                EventHandler<WindowEventArgs> handler;
                lock (gate)
                {
                    handler = item.Kind switch
                    {
                        WindowEventKind.Activated => activated,
                        WindowEventKind.TextChanged => textChanged,
                        WindowEventKind.Minimized => minimized,
                        WindowEventKind.Unminimized => unminimized,
                        _ => null
                    };
                }

                try
                {
                    handler?.Invoke(this, new WindowEventArgs(item.Element));
                }
                catch
                {
                    // swallow
                }
            }
        }

        private void TeardownNative()
        {
            try
            {
                MacRunLoopHost.Shared.RunOnLoop(() =>
                {
                    if (observer != IntPtr.Zero)
                    {
                        var source = MacNative.AXObserverGetRunLoopSource(observer);
                        if (source != IntPtr.Zero)
                        {
                            MacNative.CFRunLoopRemoveSource(
                                MacRunLoopHost.Shared.RunLoop,
                                source,
                                MacNative.KCFRunLoopCommonModes);
                        }

                        MacNative.CFRelease(observer);
                        observer = IntPtr.Zero;
                    }

                    if (systemWide != IntPtr.Zero)
                    {
                        MacNative.CFRelease(systemWide);
                        systemWide = IntPtr.Zero;
                    }

                    ReleaseCF(ref notifFocused);
                    ReleaseCF(ref notifTitle);
                    ReleaseCF(ref notifValue);
                    ReleaseCF(ref attrTitle);
                    observerCallback = null;
                });
            }
            catch
            {
                observer = IntPtr.Zero;
                systemWide = IntPtr.Zero;
                observerCallback = null;
            }
        }

        private static void ReleaseCF(ref IntPtr cf)
        {
            if (cf != IntPtr.Zero)
            {
                MacNative.CFRelease(cf);
                cf = IntPtr.Zero;
            }
        }

        private enum WindowEventKind
        {
            Activated,
            TextChanged,
            Minimized,
            Unminimized
        }

        private readonly struct WindowEventItem
        {
            internal WindowEventItem(WindowEventKind kind, IntPtr element)
            {
                Kind = kind;
                Element = element;
            }

            internal WindowEventKind Kind { get; }
            internal IntPtr Element { get; }
        }
    }
}
