using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventHook.Helpers;
using EventHook.Hooks;
using EventHook.Hooks.Library;

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
        private const uint EVENT_SYSTEM_DIALOGSTART = 0x0010;
        private const uint EVENT_SYSTEM_DIALOGEND = 0x0011;
        private const uint WINEVENT_OUTOFCONTEXT = 0;

        private readonly object accesslock = new object();
        private readonly SyncFactory factory;
        private Dictionary<IntPtr, WindowData> activeWindows;
        private AsyncConcurrentQueue<object> appQueue;
        private bool isRunning;
        private bool disposed;
        private bool lastEventWasLaunched;
        private IntPtr lastHwndLaunched;
        private CancellationTokenSource taskCancellationTokenSource;
        private WindowHook windowHook;
        private IntPtr dialogStartHook = IntPtr.Zero;
        private IntPtr dialogEndHook = IntPtr.Zero;
        private WinEventProc dialogProc;

        internal ApplicationWatcher(SyncFactory factory)
        {
            this.factory = factory;
        }

        public event EventHandler<ApplicationEventArgs> OnApplicationWindowChange;

        public void Start()
        {
            lock (accesslock)
            {
                if (isRunning)
                {
                    return;
                }

                activeWindows = new Dictionary<IntPtr, WindowData>();
                taskCancellationTokenSource = new CancellationTokenSource();
                appQueue = new AsyncConcurrentQueue<object>(taskCancellationTokenSource.Token);

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

                lastEventWasLaunched = false;
                lastHwndLaunched = IntPtr.Zero;
                Task.Factory.StartNew(AppConsumer);
                isRunning = true;
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

                appQueue.Enqueue(false);
                isRunning = false;
                taskCancellationTokenSource.Cancel();
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

                // Dialogs often lack WS_SYSMENU; still raise when IncludeDialogs is on.
                if (eventType == EVENT_SYSTEM_DIALOGSTART)
                {
                    appQueue?.Enqueue(new WindowData { HWnd = hwnd, EventType = 0 });
                }
                else if (eventType == EVENT_SYSTEM_DIALOGEND)
                {
                    appQueue?.Enqueue(new WindowData { HWnd = hwnd, EventType = 2 });
                }
            }
            catch
            {
                // never throw from win event
            }
        }

        private void WindowCreated(ShellHook shellObject, IntPtr hWnd) =>
            appQueue?.Enqueue(new WindowData { HWnd = hWnd, EventType = 0 });

        private void WindowDestroyed(ShellHook shellObject, IntPtr hWnd) =>
            appQueue?.Enqueue(new WindowData { HWnd = hWnd, EventType = 2 });

        private void WindowActivated(ShellHook shellObject, IntPtr hWnd) =>
            appQueue?.Enqueue(new WindowData { HWnd = hWnd, EventType = 1 });

        private async Task AppConsumer()
        {
            while (isRunning)
            {
                object item;
                try
                {
                    item = await appQueue.DequeueAsync();
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (item is null)
                {
                    continue;
                }

                if (item is bool)
                {
                    break;
                }

                var wnd = (WindowData)item;
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
                // Window became relevant after creation (visibility) — treat as launched then activated.
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
                // Still notify for dialogs tracked only via WinEvent
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
    }
}
