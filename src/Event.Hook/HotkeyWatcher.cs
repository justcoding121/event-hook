using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventHook.Helpers;

#if WINDOWS
using System.Runtime.InteropServices;
using System.Threading.Channels;
using System.Windows.Forms;
using EventHook.Hooks.Library;
#else
using System.Threading.Channels;
using EventHook.Platforms.Linux;
using EventHook.Platforms.Mac;
#endif

namespace EventHook
{
    /// <summary>
    /// Arguments for a registered hotkey press.
    /// </summary>
    public class HotkeyEventArgs : EventArgs
    {
        public object Id { get; set; }
        public Hotkey Hotkey { get; set; }
    }

    /// <summary>
    /// Global hotkey watcher using RegisterHotKey / WM_HOTKEY on the shared message pump.
    /// </summary>
    public class HotkeyWatcher : IDisposable
    {
        private readonly object accesslock = new object();
        private readonly SyncFactory factory;
        private bool isRunning;
        private bool disposed;

#if WINDOWS
        private readonly Dictionary<int, (object Id, Hotkey Hotkey)> registrations =
            new Dictionary<int, (object, Hotkey)>();
        private int nextId = 1;
        private MessageHandler pumpWindow;
        private EventOffload<int> offload;
        private CancellationTokenSource taskCancellationTokenSource;
#else
        private readonly Dictionary<int, (object Id, Hotkey Hotkey)> registrations =
            new Dictionary<int, (object, Hotkey)>();
        private int nextId = 1;
        private MacHotkey macHotkey;
        private LinuxHotkeyX11 linuxHotkey;
        private EventOffload<int> offload;
        private CancellationTokenSource taskCancellationTokenSource;
#endif

        internal HotkeyWatcher(SyncFactory factory)
        {
            this.factory = factory;
        }

#pragma warning disable CS0067
        public event EventHandler<HotkeyEventArgs> OnHotkeyPressed;
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

        /// <summary>
        /// Register a hotkey. The same <paramref name="id"/> is returned in <see cref="OnHotkeyPressed"/>.
        /// </summary>
        public HookStartResult Register(object id, Hotkey hotkey)
        {
            if (id == null)
            {
                throw new ArgumentNullException(nameof(id));
            }

#if WINDOWS
            var start = Start();
            if (!start.Success)
            {
                return start;
            }

            lock (accesslock)
            {
                var nativeId = nextId++;
                var (modifiers, vk) = Split(hotkey);
                var registered = false;
                var lastError = 0;

                factory.RunOnPump(() =>
                {
                    var hwnd = factory.GetMessageHandler()?.Handle ?? factory.GetHandle();
                    registered = User32.RegisterHotKey(hwnd, nativeId, modifiers, vk);
                    if (!registered)
                    {
                        lastError = Marshal.GetLastWin32Error();
                    }
                });

                if (!registered)
                {
                    return HookStartResult.Fail(
                        HookFailureReason.AlreadyInUse,
                        $"Failed to register hotkey {hotkey}. Win32 error: {lastError}");
                }

                registrations[nativeId] = (id, hotkey);
                return HookStartResult.Ok();
            }
#else
            if (OperatingSystem.IsWindows())
            {
                return PlatformSupport.WindowsOnlyTfm();
            }

            if (OperatingSystem.IsMacOS())
            {
                var start = Start();
                if (!start.Success)
                {
                    return start;
                }

                lock (accesslock)
                {
                    var nativeId = nextId++;
                    var result = macHotkey.Register((uint)nativeId, hotkey);
                    if (!result.Success)
                    {
                        return result;
                    }

                    registrations[nativeId] = (id, hotkey);
                    return HookStartResult.Ok();
                }
            }

            if (OperatingSystem.IsLinux())
            {
                var start = Start();
                if (!start.Success)
                {
                    return start;
                }

                lock (accesslock)
                {
                    return linuxHotkey.Register(id, hotkey);
                }
            }

            return PlatformSupport.NotSupportedYet("Hotkey", PlatformSupport.CurrentOsName);
#endif
        }

        /// <summary>
        /// Register a hotkey with a typed identifier.
        /// </summary>
        public HookStartResult Register<T>(T id, Hotkey hotkey) => Register((object)id, hotkey);

        /// <summary>
        /// Unregister by the same id used in <see cref="Register"/>.
        /// </summary>
        public void Unregister(object id)
        {
#if WINDOWS
            lock (accesslock)
            {
                int? nativeId = null;
                foreach (var pair in registrations)
                {
                    if (Equals(pair.Value.Id, id))
                    {
                        nativeId = pair.Key;
                        break;
                    }
                }

                if (nativeId == null)
                {
                    return;
                }

                factory.RunOnPump(() =>
                {
                    var hwnd = factory.GetMessageHandler()?.Handle ?? factory.GetHandle();
                    User32.UnregisterHotKey(hwnd, nativeId.Value);
                });
                registrations.Remove(nativeId.Value);
            }
#else
            if (OperatingSystem.IsMacOS())
            {
                lock (accesslock)
                {
                    int? nativeId = null;
                    foreach (var pair in registrations)
                    {
                        if (Equals(pair.Value.Id, id))
                        {
                            nativeId = pair.Key;
                            break;
                        }
                    }

                    if (nativeId == null)
                    {
                        return;
                    }

                    macHotkey?.Unregister((uint)nativeId.Value);
                    registrations.Remove(nativeId.Value);
                }

                return;
            }

            if (OperatingSystem.IsLinux())
            {
                linuxHotkey?.Unregister(id);
            }
#endif
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
                taskCancellationTokenSource = new CancellationTokenSource();
                offload = new EventOffload<int>();

                try
                {
                    factory.RunOnPump(() =>
                    {
                        pumpWindow = factory.GetMessageHandler();
                        if (pumpWindow != null)
                        {
                            pumpWindow.MessageReceived += OnPumpMessage;
                        }
                    });
                }
                catch (Exception ex)
                {
                    offload?.Dispose();
                    offload = null;
                    taskCancellationTokenSource?.Dispose();
                    taskCancellationTokenSource = null;
                    return HookStartResult.Fail(HookFailureReason.NativeFailure, ex.Message);
                }

                Task.Factory.StartNew(HotkeyConsumerAsync, TaskCreationOptions.LongRunning);
                isRunning = true;
                return HookStartResult.Ok();
#else
                if (OperatingSystem.IsWindows())
                {
                    return PlatformSupport.WindowsOnlyTfm();
                }

                if (OperatingSystem.IsMacOS())
                {
                    taskCancellationTokenSource = new CancellationTokenSource();
                    offload = new EventOffload<int>();
                    macHotkey = new MacHotkey();
                    var macResult = macHotkey.Start(nativeId => offload?.TryWrite(nativeId));
                    if (!macResult.Success)
                    {
                        macHotkey.Dispose();
                        macHotkey = null;
                        offload?.Dispose();
                        offload = null;
                        taskCancellationTokenSource?.Dispose();
                        taskCancellationTokenSource = null;
                        return macResult;
                    }

                    Task.Factory.StartNew(HotkeyConsumerAsync, TaskCreationOptions.LongRunning);
                    isRunning = true;
                    return HookStartResult.Ok();
                }

                if (OperatingSystem.IsLinux())
                {
                    taskCancellationTokenSource = new CancellationTokenSource();
                    offload = new EventOffload<int>();
                    linuxHotkey = new LinuxHotkeyX11(nativeId => offload?.TryWrite(nativeId));
                    var linuxResult = linuxHotkey.Start();
                    if (!linuxResult.Success)
                    {
                        linuxHotkey.Dispose();
                        linuxHotkey = null;
                        offload?.Dispose();
                        offload = null;
                        taskCancellationTokenSource?.Dispose();
                        taskCancellationTokenSource = null;
                        return linuxResult;
                    }

                    Task.Factory.StartNew(HotkeyConsumerAsync, TaskCreationOptions.LongRunning);
                    isRunning = true;
                    return HookStartResult.Ok();
                }

                return PlatformSupport.NotSupportedYet("Hotkey", PlatformSupport.CurrentOsName);
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
                    var hwnd = factory.GetMessageHandler()?.Handle ?? factory.GetHandle();
                    foreach (var id in registrations.Keys)
                    {
                        User32.UnregisterHotKey(hwnd, id);
                    }

                    if (pumpWindow != null)
                    {
                        pumpWindow.MessageReceived -= OnPumpMessage;
                        pumpWindow = null;
                    }
                });

                registrations.Clear();
                isRunning = false;
                offload?.Complete();
                taskCancellationTokenSource?.Cancel();
                taskCancellationTokenSource?.Dispose();
                taskCancellationTokenSource = null;
                offload?.Dispose();
                offload = null;
#else
                if (OperatingSystem.IsMacOS())
                {
                    macHotkey?.Stop();
                    macHotkey?.Dispose();
                    macHotkey = null;
                    registrations.Clear();
                }
                else if (OperatingSystem.IsLinux())
                {
                    linuxHotkey?.Dispose();
                    linuxHotkey = null;
                }

                isRunning = false;
                offload?.Complete();
                taskCancellationTokenSource?.Cancel();
                taskCancellationTokenSource?.Dispose();
                taskCancellationTokenSource = null;
                offload?.Dispose();
                offload = null;
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
        private void OnPumpMessage(Message msg)
        {
            if (msg.Msg != HotkeyNative.WM_HOTKEY)
            {
                return;
            }

            var nativeId = msg.WParam.ToInt32();
            offload?.TryWrite(nativeId);
        }

        private static (uint modifiers, uint vk) Split(Hotkey hotkey)
        {
            uint modifiers = 0;
            if (hotkey.Modifiers.HasFlag(KeyModifiers.Alt))
            {
                modifiers |= HotkeyNative.MOD_ALT;
            }

            if (hotkey.Modifiers.HasFlag(KeyModifiers.Control))
            {
                modifiers |= HotkeyNative.MOD_CONTROL;
            }

            if (hotkey.Modifiers.HasFlag(KeyModifiers.Shift))
            {
                modifiers |= HotkeyNative.MOD_SHIFT;
            }

            if (hotkey.Modifiers.HasFlag(KeyModifiers.Meta))
            {
                modifiers |= HotkeyNative.MOD_WIN;
            }

            modifiers |= HotkeyNative.MOD_NOREPEAT;
            return (modifiers, (uint)hotkey.Key);
        }
#endif

        private async Task HotkeyConsumerAsync()
        {
            var token = taskCancellationTokenSource.Token;
            while (!token.IsCancellationRequested)
            {
                int nativeId;
                try
                {
                    nativeId = await offload.ReadAsync(token).ConfigureAwait(false);
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

                object entryId;
                Hotkey entryHotkey;
                lock (accesslock)
                {
#if WINDOWS
                    if (!registrations.TryGetValue(nativeId, out var entry))
                    {
                        continue;
                    }

                    entryId = entry.Id;
                    entryHotkey = entry.Hotkey;
#else
                    if (OperatingSystem.IsLinux())
                    {
                        if (linuxHotkey == null ||
                            !linuxHotkey.TryGetRegistration(nativeId, out entryId, out entryHotkey))
                        {
                            continue;
                        }
                    }
                    else if (!registrations.TryGetValue(nativeId, out var entry))
                    {
                        continue;
                    }
                    else
                    {
                        entryId = entry.Id;
                        entryHotkey = entry.Hotkey;
                    }
#endif
                }

                try
                {
                    OnHotkeyPressed?.Invoke(this, new HotkeyEventArgs { Id = entryId, Hotkey = entryHotkey });
                }
                catch
                {
                    // never throw from consumer
                }
            }
        }
    }
}
