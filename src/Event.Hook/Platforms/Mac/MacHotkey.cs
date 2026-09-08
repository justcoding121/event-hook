using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using EventHook.Helpers;
using EventHook.Platforms.Mac.Native;

namespace EventHook.Platforms.Mac
{
    /// <summary>
    /// Carbon <c>RegisterEventHotKey</c> backend. Callback only enqueues the native hotkey id.
    /// </summary>
    internal sealed class MacHotkey : IDisposable
    {
        private const uint Signature = 0x4556484B; // 'EVHK'

        private const ulong CgShift = 0x00020000;
        private const ulong CgControl = 0x00040000;
        private const ulong CgAlternate = 0x00080000;
        private const ulong CgCommand = 0x00100000;

        private readonly Dictionary<uint, IntPtr> hotKeyRefs = new Dictionary<uint, IntPtr>();
        private readonly Dictionary<uint, (uint MacKey, ulong CgFlags)> tapHotkeys = new Dictionary<uint, (uint, ulong)>();
        private MacNative.EventHandlerProc handlerProc;
        private IntPtr handlerRef;
        private Action<int> onHotkey;
        private bool disposed;
        private bool handlerInstalled;

        internal HookStartResult Start(Action<int> enqueue)
        {
            if (enqueue == null)
            {
                throw new ArgumentNullException(nameof(enqueue));
            }

            onHotkey = enqueue;
            MacHotkeyTap.SetSink(this);
            var tap = MacKeyboardMouseHub.EnsureTap();
            if (!tap.Success)
            {
                return tap;
            }
            if (handlerInstalled)
            {
                return HookStartResult.Ok();
            }

            HookStartResult result = HookStartResult.Ok();
            try
            {
                MacRunLoopHost.Shared.RunOnLoop(() =>
                {
                    handlerProc = OnHotKeyEvent;
                    var types = new[]
                    {
                        new MacNative.EventTypeSpec
                        {
                            eventClass = MacNative.kEventClassKeyboard,
                            eventKind = MacNative.kEventHotKeyPressed
                        }
                    };

                    var status = MacNative.InstallEventHandler(
                        MacNative.GetEventDispatcherTarget(),
                        handlerProc,
                        types.Length,
                        types,
                        IntPtr.Zero,
                        out handlerRef);

                    if (status != 0)
                    {
                        result = HookStartResult.Fail(
                            HookFailureReason.NativeFailure,
                            $"InstallEventHandler failed with OSStatus {status}.");
                        return;
                    }

                    handlerInstalled = true;
                });
            }
            catch (Exception ex)
            {
                return HookStartResult.Fail(HookFailureReason.NativeFailure, ex.Message);
            }

            return result;
        }

        internal HookStartResult Register(uint nativeId, Hotkey hotkey)
        {
            if (!MacKeyCodeMap.TryToMacKeyCode(hotkey.Key, out var macKey))
            {
                return HookStartResult.Fail(
                    HookFailureReason.NativeFailure,
                    $"Hotkey key {hotkey.Key} is not mapped on macOS.");
            }

            var modifiers = ToCarbonModifiers(hotkey.Modifiers);
            HookStartResult result = HookStartResult.Ok();

            MacRunLoopHost.Shared.RunOnLoop(() =>
            {
                var id = new MacNative.EventHotKeyID { signature = Signature, id = nativeId };
                var status = MacNative.RegisterEventHotKey(
                    macKey,
                    modifiers,
                    id,
                    MacNative.GetEventDispatcherTarget(),
                    0,
                    out var hotKeyRef);

                tapHotkeys[nativeId] = (macKey, ToCgFlags(hotkey.Modifiers));
                if (status != 0 || hotKeyRef == IntPtr.Zero)
                {
                    // Carbon may refuse a combo that the event tap can still observe.
                    return;
                }

                hotKeyRefs[nativeId] = hotKeyRef;
            });

            return result;
        }

        internal void Unregister(uint nativeId)
        {
            MacRunLoopHost.Shared.RunOnLoop(() =>
            {
                if (!hotKeyRefs.TryGetValue(nativeId, out var href))
                {
                    return;
                }

                MacNative.UnregisterEventHotKey(href);
                hotKeyRefs.Remove(nativeId);
                tapHotkeys.Remove(nativeId);
            });
        }

        internal void Stop()
        {
            try
            {
                MacRunLoopHost.Shared.RunOnLoop(() =>
                {
                    foreach (var href in hotKeyRefs.Values)
                    {
                        try
                        {
                            MacNative.UnregisterEventHotKey(href);
                        }
                        catch
                        {
                            // ignore
                        }
                    }

                    hotKeyRefs.Clear();
                    tapHotkeys.Clear();

                    if (handlerRef != IntPtr.Zero)
                    {
                        MacNative.RemoveEventHandler(handlerRef);
                        handlerRef = IntPtr.Zero;
                    }

                    handlerInstalled = false;
                    handlerProc = null;
                });
            }
            catch
            {
                hotKeyRefs.Clear();
                tapHotkeys.Clear();
                handlerRef = IntPtr.Zero;
                handlerInstalled = false;
                handlerProc = null;
            }

            onHotkey = null;
            MacHotkeyTap.SetSink(null);
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

        private int OnHotKeyEvent(IntPtr nextHandler, IntPtr theEvent, IntPtr userData)
        {
            _ = nextHandler;
            _ = userData;
            try
            {
                if (MacNative.GetEventParameter(
                        theEvent,
                        MacNative.kEventParamDirectObject,
                        MacNative.typeEventHotKeyID,
                        IntPtr.Zero,
                        Marshal.SizeOf<MacNative.EventHotKeyID>(),
                        IntPtr.Zero,
                        out var hotKeyId) == 0)
                {
                    onHotkey?.Invoke((int)hotKeyId.id);
                }
            }
            catch
            {
                // never throw from carbon handler
            }

            return 0; // noErr
        }

        private static uint ToCarbonModifiers(KeyModifiers modifiers)
        {
            uint carbon = 0;
            if (modifiers.HasFlag(KeyModifiers.Alt))
            {
                carbon |= MacNative.optionKey;
            }

            if (modifiers.HasFlag(KeyModifiers.Control))
            {
                carbon |= MacNative.controlKey;
            }

            if (modifiers.HasFlag(KeyModifiers.Shift))
            {
                carbon |= MacNative.shiftKey;
            }

            if (modifiers.HasFlag(KeyModifiers.Meta))
            {
                carbon |= MacNative.cmdKey;
            }

            return carbon;
        }

        private static ulong ToCgFlags(KeyModifiers modifiers)
        {
            ulong flags = 0;
            if (modifiers.HasFlag(KeyModifiers.Shift))
            {
                flags |= CgShift;
            }

            if (modifiers.HasFlag(KeyModifiers.Control))
            {
                flags |= CgControl;
            }

            if (modifiers.HasFlag(KeyModifiers.Alt))
            {
                flags |= CgAlternate;
            }

            if (modifiers.HasFlag(KeyModifiers.Meta))
            {
                flags |= CgCommand;
            }

            return flags;
        }

        internal void TryDispatchTap(int macKeyCode, ulong cgFlags, int eventType)
        {
            if (eventType != 0)
            {
                return;
            }

            foreach (var pair in tapHotkeys)
            {
                if ((int)pair.Value.MacKey == macKeyCode && (cgFlags & pair.Value.CgFlags) == pair.Value.CgFlags)
                {
                    onHotkey?.Invoke((int)pair.Key);
                    return;
                }
            }
        }
    }

    /// <summary>
    /// CGEventTap delivers synthetic hotkeys that Carbon <c>RegisterEventHotKey</c> misses.
    /// </summary>
    internal static class MacHotkeyTap
    {
        private static MacHotkey sink;

        internal static void SetSink(MacHotkey value) => sink = value;

        internal static void TryDispatch(int macKeyCode, ulong cgFlags, int eventType) =>
            sink?.TryDispatchTap(macKeyCode, cgFlags, eventType);
    }
}
