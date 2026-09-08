using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventHook.Helpers;

#if WINDOWS
using System.Threading.Channels;
using EventHook.Hooks;
using EventHook.Hooks.Library;
#endif

namespace EventHook
{
    public enum ApplicationEvents
    {
        Launched,
        Closed,
        Activated
    }

    public class WindowData
    {
        public int EventType;
        public IntPtr HWnd;
        public string AppPath { get; set; }
        public string AppName { get; set; }
        public string AppTitle { get; set; }
    }

    public class ApplicationEventArgs : EventArgs
    {
        public WindowData ApplicationData { get; set; }
        public ApplicationEvents Event { get; set; }
    }

    /// <summary>
    /// Watches top-level application windows and common dialogs (including MessageBox).
    /// </summary>
    public class ApplicationWatcher : IDisposable
    {
        private readonly object accesslock = new object();
        private readonly SyncFactory factory;
        private bool isRunning;
        private bool disposed;

#if WINDOWS
        private const uint EVENT_SYSTEM_DIALOGSTART = 0x0010;
        private const uint EVENT_SYSTEM_DIALOGEND = 0x0011;
        private const uint WINEVENT_OUTOFCONTEXT = 0;

        private Dictionary<IntPtr, WindowData> activeWindows;
        private EventOffload<WindowSnapshot> offload;
        private bool lastEventWasLaunched;
        private IntPtr lastHwndLaunched;
        private CancellationTokenSource taskCancellationTokenSource;
        private WindowHook windowHook;
        private IntPtr dialogStartHook = IntPtr.Zero;
        private IntPtr dialogEndHook = IntPtr.Zero;
        private WinEventProc dialogProc;

        private readonly struct WindowSnapshot
        {
            internal WindowSnapshot(IntPtr hwnd, int eventType)
            {
                HWnd = hwnd;
                EventType = eventType;
            }

            internal IntPtr HWnd { get; }
            internal int EventType { get; }
        }
#endif

        internal ApplicationWatcher(SyncFactory factory)
        {
            this.factory = factory;
        }

#pragma warning disable CS0067 // Raised only on Windows implementation
        public event EventHandler<ApplicationEventArgs> OnApplicationWindowChange;
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
                activeWindows = new Dictionary<IntPtr, WindowData>();
                taskCancellationTokenSource = new CancellationTokenSource();
                offload = new EventOffload<WindowSnapshot>();
                lastEventWasLaunched = false;
                lastHwndLaunched = IntPtr.Zero;

                try
                {
                    factory.RunOnPump(() =>
                    {
                        windowHook = new WindowHook(factory);
                        windowHook.WindowCreated += WindowCreated;
                        windowHook.WindowDestroyed += WindowDestroyed;
                        windowHook.WindowActivated += WindowActivated;

                        dialogProc = OnDialogWinEvent;
                        dialogStartHook = User32.SetWinEventHook(EVENT_SYSTEM_DIALOGSTART, EVENT_SYSTEM_DIALOGSTART,
                            IntPtr.Zero, dialogProc, 0, 0, WINEVENT_OUTOFCONTEXT);
                        dialogEndHook = User32.SetWinEventHook(EVENT_SYSTEM_DIALOGEND, EVENT_SYSTEM_DIALOGEND,
                            IntPtr.Zero, dialogProc, 0, 0, WINEVENT_OUTOFCONTEXT);
                    });
                }
                catch (Exception ex)
                {
                    CleanupFailedStart();
                    return HookStartResult.Fail(HookFailureReason.NativeFailure, ex.Message);
                }

                Task.Factory.StartNew(AppConsumer, TaskCreationOptions.LongRunning);
                isRunning = true;
                return HookStartResult.Ok();
#else
                if (OperatingSystem.IsWindows())
                {
                    return PlatformSupport.WindowsOnlyTfm();
                }

                return PlatformSupport.NotSupportedYet("Application", PlatformSupport.CurrentOsName);
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
                    if (windowHook != null)
                    {
                        windowHook.WindowCreated -= WindowCreated;
                        windowHook.WindowDestroyed -= WindowDestroyed;
                        windowHook.WindowActivated -= WindowActivated;
                        windowHook.Destroy();
                        windowHook = null;
                    }

                    if (dialogStartHook != IntPtr.Zero)
                    {
                        User32.UnhookWinEvent(dialogStartHook);
                        dialogStartHook = IntPtr.Zero;
                    }

                    if (dialogEndHook != IntPtr.Zero)
                    {
                        User32.UnhookWinEvent(dialogEndHook);
                        dialogEndHook = IntPtr.Zero;
                    }

                    dialogProc = null;
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
                    if (windowHook != null)
                    {
                        windowHook.WindowCreated -= WindowCreated;
                        windowHook.WindowDestroyed -= WindowDestroyed;
                        windowHook.WindowActivated -= WindowActivated;
                        windowHook.Destroy();
                        windowHook = null;
                    }

                    if (dialogStartHook != IntPtr.Zero)
                    {
                        User32.UnhookWinEvent(dialogStartHook);
                        dialogStartHook = IntPtr.Zero;
                    }

                    if (dialogEndHook != IntPtr.Zero)
                    {
                        User32.UnhookWinEvent(dialogEndHook);
                        dialogEndHook = IntPtr.Zero;
                    }

                    dialogProc = null;
                });
            }
            catch
            {
                windowHook = null;
                dialogStartHook = IntPtr.Zero;
                dialogEndHook = IntPtr.Zero;
                dialogProc = null;
            }

            offload?.Dispose();
            offload = null;
            taskCancellationTokenSource?.Dispose();
            taskCancellationTokenSource = null;
            activeWindows = null;
        }

        private void OnDialogWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild,
            uint dwEventThread, uint dwmsEventTime)
        {
            try
            {
                if (hwnd == IntPtr.Zero || idObject != 0)
                {
                    return;
                }

                if (!AppWindowFilter.IsAppWindow(hwnd) && !AppWindowFilter.IncludeDialogs)
                {
                    return;
                }

                if (eventType == EVENT_SYSTEM_DIALOGSTART)
                {
                    offload?.TryWrite(new WindowSnapshot(hwnd, 0));
                }
                else if (eventType == EVENT_SYSTEM_DIALOGEND)
                {
                    offload?.TryWrite(new WindowSnapshot(hwnd, 2));
                }
            }
            catch
            {
                // never throw from win event
            }
        }

        private void WindowCreated(ShellHook shellObject, IntPtr hWnd) =>
            offload?.TryWrite(new WindowSnapshot(hWnd, 0));

        private void WindowDestroyed(ShellHook shellObject, IntPtr hWnd) =>
            offload?.TryWrite(new WindowSnapshot(hWnd, 2));

        private void WindowActivated(ShellHook shellObject, IntPtr hWnd) =>
            offload?.TryWrite(new WindowSnapshot(hWnd, 1));

        private async Task AppConsumer()
        {
            var token = taskCancellationTokenSource.Token;
            while (!token.IsCancellationRequested)
            {
                WindowSnapshot snap;
                try
                {
                    snap = await offload.ReadAsync(token).ConfigureAwait(false);
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

                var wnd = new WindowData { HWnd = snap.HWnd, EventType = snap.EventType };
                switch (wnd.EventType)
                {
                    case 0:
                        OnWindowCreated(wnd);
                        break;
                    case 1:
                        OnWindowActivated(wnd);
                        break;
                    case 2:
                        OnWindowDestroyed(wnd);
                        break;
                }
            }
        }

        private void OnWindowCreated(WindowData wnd)
        {
            if (!activeWindows.ContainsKey(wnd.HWnd))
            {
                activeWindows.Add(wnd.HWnd, wnd);
            }

            ApplicationStatus(wnd, ApplicationEvents.Launched);
            lastEventWasLaunched = true;
            lastHwndLaunched = wnd.HWnd;
        }

        private void OnWindowActivated(WindowData wnd)
        {
            if (activeWindows.ContainsKey(wnd.HWnd))
            {
                if (!lastEventWasLaunched && lastHwndLaunched != wnd.HWnd)
                {
                    ApplicationStatus(activeWindows[wnd.HWnd], ApplicationEvents.Activated);
                }
            }
            else if (AppWindowFilter.IsAppWindow(wnd.HWnd))
            {
                OnWindowCreated(wnd);
            }

            lastEventWasLaunched = false;
        }

        private void OnWindowDestroyed(WindowData wnd)
        {
            if (activeWindows.ContainsKey(wnd.HWnd))
            {
                ApplicationStatus(activeWindows[wnd.HWnd], ApplicationEvents.Closed);
                activeWindows.Remove(wnd.HWnd);
            }
            else
            {
                ApplicationStatus(wnd, ApplicationEvents.Closed);
            }

            lastEventWasLaunched = false;
        }

        private void ApplicationStatus(WindowData wnd, ApplicationEvents appEvent)
        {
            try
            {
                wnd.AppTitle = appEvent == ApplicationEvents.Closed ? wnd.AppTitle : WindowHelper.GetWindowText(wnd.HWnd);
                wnd.AppPath = appEvent == ApplicationEvents.Closed ? wnd.AppPath : WindowHelper.GetAppPath(wnd.HWnd);
                wnd.AppName = appEvent == ApplicationEvents.Closed
                    ? wnd.AppName
                    : WindowHelper.GetAppDescription(wnd.AppPath);

                OnApplicationWindowChange?.Invoke(this,
                    new ApplicationEventArgs { ApplicationData = wnd, Event = appEvent });
            }
            catch
            {
                // swallow
            }
        }
#endif
    }
}
