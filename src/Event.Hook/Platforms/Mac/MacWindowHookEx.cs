using System;
using System.Collections.Generic;
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
        private readonly Dictionary<int, AppObserver> observers = new Dictionary<int, AppObserver>();
        private readonly int[] pidScratch = new int[4096];
        private EventOffload<WindowEventItem> offload;
        private CancellationTokenSource cts;
        private Timer refreshTimer;
        private MacNative.AXObserverCallback observerCallback;
        private IntPtr notifFocused;
        private IntPtr notifTitle;
        private IntPtr notifMiniaturized;
        private IntPtr notifDeminiaturized;
        private IntPtr notifAppActivated;
        private IntPtr attrFocusedWindow;
        private IntPtr attrTitle;
        private IntPtr attrMinimized;
        private int lastActivePid = -1;
        private string lastTitle = string.Empty;
        private bool lastMinimized;
        private bool pollPrimed;
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
                        notifFocused = MacNative.CFStringCreateWithCString(IntPtr.Zero, "AXFocusedWindowChanged", 0x08000100);
                        notifTitle = MacNative.CFStringCreateWithCString(IntPtr.Zero, "AXTitleChanged", 0x08000100);
                        notifMiniaturized = MacNative.CFStringCreateWithCString(IntPtr.Zero, "AXWindowMiniaturized", 0x08000100);
                        notifDeminiaturized = MacNative.CFStringCreateWithCString(IntPtr.Zero, "AXWindowDeminiaturized", 0x08000100);
                        notifAppActivated = MacNative.CFStringCreateWithCString(IntPtr.Zero, "AXApplicationActivated", 0x08000100);
                        attrFocusedWindow = MacNative.CFStringCreateWithCString(IntPtr.Zero, "AXFocusedWindow", 0x08000100);
                        attrTitle = MacNative.CFStringCreateWithCString(IntPtr.Zero, "AXTitle", 0x08000100);
                        attrMinimized = MacNative.CFStringCreateWithCString(IntPtr.Zero, "AXMinimized", 0x08000100);
                        observerCallback = OnAxNotification;
                        RefreshObserversUnlocked();
                        PollFrontWindowUnlocked();
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
                refreshTimer = new Timer(
                    _ =>
                    {
                        try
                        {
                            MacRunLoopHost.Shared.RunOnLoop(() =>
                            {
                                RefreshObserversUnlocked();
                                PollFrontWindowUnlocked();
                            });
                        }
                        catch
                        {
                            // ignore
                        }
                    },
                    null,
                    TimeSpan.FromMilliseconds(500),
                    TimeSpan.FromMilliseconds(500));
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
                refreshTimer?.Dispose();
                refreshTimer = null;
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
                if (notification == notifFocused || notification == notifAppActivated)
                {
                    kind = WindowEventKind.Activated;
                }
                else if (notification == notifTitle)
                {
                    kind = WindowEventKind.TextChanged;
                }
                else if (notification == notifMiniaturized)
                {
                    kind = WindowEventKind.Minimized;
                }
                else if (notification == notifDeminiaturized)
                {
                    kind = WindowEventKind.Unminimized;
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
                    foreach (var pid in new List<int>(observers.Keys))
                    {
                        DetachObserverUnlocked(pid);
                    }

                    observers.Clear();
                    ReleaseCF(ref notifFocused);
                    ReleaseCF(ref notifTitle);
                    ReleaseCF(ref notifMiniaturized);
                    ReleaseCF(ref notifDeminiaturized);
                    ReleaseCF(ref notifAppActivated);
                    ReleaseCF(ref attrFocusedWindow);
                    ReleaseCF(ref attrTitle);
                    ReleaseCF(ref attrMinimized);
                    observerCallback = null;
                    lastActivePid = -1;
                    lastTitle = string.Empty;
                    lastMinimized = false;
                    pollPrimed = false;
                });
            }
            catch
            {
                observers.Clear();
                observerCallback = null;
            }
        }

        private void RefreshObserversUnlocked()
        {
            var seen = new HashSet<int>();
            var written = MacNative.proc_listpids(MacNative.PROC_ALL_PIDS, 0, pidScratch, pidScratch.Length * sizeof(int));
            if (written <= 0)
            {
                return;
            }

            var cls = MacObjC.GetClass("NSRunningApplication");
            var selByPid = MacObjC.Sel("runningApplicationWithProcessIdentifier:");
            var selPolicy = MacObjC.Sel("activationPolicy");
            var count = written / sizeof(int);
            for (var i = 0; i < count; i++)
            {
                var pid = pidScratch[i];
                if (pid <= 0)
                {
                    continue;
                }

                var app = MacNative.objc_msgSend_int(cls, selByPid, pid);
                if (app == IntPtr.Zero)
                {
                    continue;
                }

                var policy = (int)MacNative.objc_msgSend_nint(app, selPolicy);
                if (!MacAppFilter.ShouldTrack(policy))
                {
                    continue;
                }

                seen.Add(pid);
                if (!observers.ContainsKey(pid))
                {
                    AttachObserverUnlocked(pid);
                }
            }

            var gone = new List<int>();
            foreach (var pid in observers.Keys)
            {
                if (!seen.Contains(pid))
                {
                    gone.Add(pid);
                }
            }

            foreach (var pid in gone)
            {
                DetachObserverUnlocked(pid);
            }
        }

        private void AttachObserverUnlocked(int pid)
        {
            var status = MacNative.AXObserverCreate(pid, observerCallback, out var observer);
            if (status != 0 || observer == IntPtr.Zero)
            {
                return;
            }

            var element = MacNative.AXUIElementCreateApplication(pid);
            if (element == IntPtr.Zero)
            {
                MacNative.CFRelease(observer);
                return;
            }

            TryAdd(observer, element, notifFocused);
            TryAdd(observer, element, notifTitle);
            TryAdd(observer, element, notifMiniaturized);
            TryAdd(observer, element, notifDeminiaturized);
            TryAdd(observer, element, notifAppActivated);

            var source = MacNative.AXObserverGetRunLoopSource(observer);
            if (source != IntPtr.Zero)
            {
                MacNative.CFRunLoopAddSource(
                    MacRunLoopHost.Shared.RunLoop, source, MacNative.KCFRunLoopDefaultMode);
                MacNative.CFRunLoopAddSource(
                    MacRunLoopHost.Shared.RunLoop, source, MacNative.KCFRunLoopCommonModes);
            }

            observers[pid] = new AppObserver(observer, element);
        }

        private void DetachObserverUnlocked(int pid)
        {
            if (!observers.TryGetValue(pid, out var item))
            {
                return;
            }

            observers.Remove(pid);
            if (item.Observer != IntPtr.Zero)
            {
                var source = MacNative.AXObserverGetRunLoopSource(item.Observer);
                if (source != IntPtr.Zero)
                {
                    MacNative.CFRunLoopRemoveSource(
                        MacRunLoopHost.Shared.RunLoop, source, MacNative.KCFRunLoopDefaultMode);
                    MacNative.CFRunLoopRemoveSource(
                        MacRunLoopHost.Shared.RunLoop, source, MacNative.KCFRunLoopCommonModes);
                }

                MacNative.CFRelease(item.Observer);
            }

            if (item.Element != IntPtr.Zero)
            {
                MacNative.CFRelease(item.Element);
            }
        }

        private void PollFrontWindowUnlocked()
        {
            var cls = MacObjC.GetClass("NSRunningApplication");
            if (cls == IntPtr.Zero)
            {
                return;
            }

            var written = MacNative.proc_listpids(MacNative.PROC_ALL_PIDS, 0, pidScratch, pidScratch.Length * sizeof(int));
            if (written <= 0)
            {
                return;
            }

            var selByPid = MacObjC.Sel("runningApplicationWithProcessIdentifier:");
            var selPolicy = MacObjC.Sel("activationPolicy");
            var selActive = MacObjC.Sel("isActive");
            var count = written / sizeof(int);
            var activePid = -1;
            for (var i = 0; i < count; i++)
            {
                var pid = pidScratch[i];
                if (pid <= 0)
                {
                    continue;
                }

                var app = MacNative.objc_msgSend_int(cls, selByPid, pid);
                if (app == IntPtr.Zero)
                {
                    continue;
                }

                var policy = (int)MacNative.objc_msgSend_nint(app, selPolicy);
                if (!MacAppFilter.ShouldTrack(policy))
                {
                    continue;
                }

                if (MacNative.objc_msgSend_bool(app, selActive))
                {
                    activePid = pid;
                    break;
                }
            }

            var title = string.Empty;
            var minimized = false;
            IntPtr element = IntPtr.Zero;
            if (activePid > 0)
            {
                var appEl = MacNative.AXUIElementCreateApplication(activePid);
                if (appEl != IntPtr.Zero)
                {
                    if (MacNative.AXUIElementCopyAttributeValue(appEl, attrFocusedWindow, out var window) == 0 &&
                        window != IntPtr.Zero)
                    {
                        element = window;
                        title = CopyAxString(window, attrTitle) ?? string.Empty;
                        minimized = CopyAxBool(window, attrMinimized);
                    }

                    MacNative.CFRelease(appEl);
                }
            }

            if (!pollPrimed)
            {
                lastActivePid = activePid;
                lastTitle = title;
                lastMinimized = minimized;
                pollPrimed = true;
                return;
            }

            if (activePid > 0 && activePid != lastActivePid)
            {
                offload?.TryWrite(new WindowEventItem(WindowEventKind.Activated, element));
            }
            else if (activePid > 0 && !string.Equals(title, lastTitle, StringComparison.Ordinal))
            {
                offload?.TryWrite(new WindowEventItem(WindowEventKind.TextChanged, element));
            }

            if (activePid > 0 && minimized != lastMinimized)
            {
                offload?.TryWrite(new WindowEventItem(
                    minimized ? WindowEventKind.Minimized : WindowEventKind.Unminimized,
                    element));
            }

            lastActivePid = activePid;
            lastTitle = title;
            lastMinimized = minimized;
        }

        private static string CopyAxString(IntPtr element, IntPtr attribute)
        {
            if (MacNative.AXUIElementCopyAttributeValue(element, attribute, out var value) != 0 || value == IntPtr.Zero)
            {
                return null;
            }

            var buffer = new byte[512];
            var ok = MacNative.CFStringGetCString(value, buffer, buffer.Length, 0x08000100);
            MacNative.CFRelease(value);
            if (!ok)
            {
                return null;
            }

            var end = Array.IndexOf(buffer, (byte)0);
            return System.Text.Encoding.UTF8.GetString(buffer, 0, end < 0 ? buffer.Length : end);
        }

        private static bool CopyAxBool(IntPtr element, IntPtr attribute)
        {
            if (MacNative.AXUIElementCopyAttributeValue(element, attribute, out var value) != 0 || value == IntPtr.Zero)
            {
                return false;
            }

            var result = MacNative.CFBooleanGetValue(value);
            MacNative.CFRelease(value);
            return result;
        }

        private static void TryAdd(IntPtr observer, IntPtr element, IntPtr notification)
        {
            if (notification != IntPtr.Zero)
            {
                MacNative.AXObserverAddNotification(observer, element, notification, IntPtr.Zero);
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

        private readonly struct AppObserver
        {
            internal AppObserver(IntPtr observer, IntPtr element)
            {
                Observer = observer;
                Element = element;
            }

            internal IntPtr Observer { get; }
            internal IntPtr Element { get; }
        }
    }
}
