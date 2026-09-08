using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using EventHook.Helpers;
using EventHook.Hooks;
using EventHook.Platforms.Mac.Native;

namespace EventHook.Platforms.Mac
{
    [InlineArray(8)]
    internal struct MacUnicodeBuffer
    {
#pragma warning disable IDE0051 // InlineArray requires a field; Sonar S1144 is a false positive
        private char element0;
#pragma warning restore IDE0051
    }

    /// <summary>
    /// Value-type keyboard snapshot captured on the CGEventTap callback (no managed allocations).
    /// </summary>
    internal struct MacKeySnapshot
    {
        internal int MacKeyCode;
        internal int VkCode;
        internal int EventType; // 0 down, 1 up
        internal ulong Flags;
        private MacUnicodeBuffer unicode; // NOSONAR S3459 - mutated via InlineArray indexer
        private int unicodeLength;

        internal void SetUnicode(ushort[] chars, int length)
        {
            var copy = length < 8 ? length : 8;
            unicodeLength = copy;
            for (var i = 0; i < copy; i++)
            {
                unicode[i] = (char)chars[i];
            }

            for (var i = copy; i < 8; i++)
            {
                unicode[i] = '\0';
            }
        }

        internal string GetUnicode()
        {
            if (unicodeLength <= 0)
            {
                return string.Empty;
            }

            return string.Create(unicodeLength, unicode, static (span, buffer) =>
            {
                for (var i = 0; i < span.Length; i++)
                {
                    span[i] = buffer[i];
                }
            });
        }
    }

    /// <summary>
    /// Value-type mouse snapshot captured on the CGEventTap callback.
    /// </summary>
    internal readonly struct MacMouseSnapshot
    {
        internal MacMouseSnapshot(MouseMessages message, Point point, uint mouseData)
        {
            Message = message;
            Point = point;
            MouseData = mouseData;
        }

        internal MouseMessages Message { get; }
        internal Point Point { get; }
        internal uint MouseData { get; }
    }

    /// <summary>
    /// CGEventTap listen-only keyboard and mouse backend.
    /// </summary>
    internal sealed class MacKeyboardMouse : IDisposable
    {
        private const string InputMonitoringPermission = "Input Monitoring";
        private readonly object gate = new object();
        private MacNative.CGEventTapCallBack tapCallback;
        private IntPtr tap;
        private IntPtr runLoopSource;
        private Action<MacKeySnapshot> onKey;
        private Action<MacMouseSnapshot> onMouse;
        private bool includeMouseMove;
        private bool disposed;
        private static readonly ushort[] UnicodeScratch = new ushort[8];

        internal HookStartResult StartKeyboard(Action<MacKeySnapshot> enqueue)
        {
            if (enqueue == null)
            {
                throw new ArgumentNullException(nameof(enqueue));
            }

            lock (gate)
            {
                onKey = enqueue;
                return EnsureTapUnlocked();
            }
        }

        internal HookStartResult StartMouse(Action<MacMouseSnapshot> enqueue, bool includeMove)
        {
            if (enqueue == null)
            {
                throw new ArgumentNullException(nameof(enqueue));
            }

            lock (gate)
            {
                onMouse = enqueue;
                includeMouseMove = includeMove;
                return EnsureTapUnlocked();
            }
        }

        internal void StopKeyboard()
        {
            lock (gate)
            {
                onKey = null;
                MaybeTeardownUnlocked();
            }
        }

        internal void StopMouse()
        {
            lock (gate)
            {
                onMouse = null;
                MaybeTeardownUnlocked();
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            lock (gate)
            {
                onKey = null;
                onMouse = null;
                TeardownUnlocked();
            }
        }

        private HookStartResult EnsureTapUnlocked()
        {
            if (tap != IntPtr.Zero)
            {
                if (!MacNative.CGEventTapIsEnabled(tap))
                {
                    TeardownUnlocked();
                    return PlatformSupport.MacPermission(InputMonitoringPermission);
                }

                return HookStartResult.Ok();
            }

            try
            {
                if (!MacNative.CGPreflightListenEventAccess())
                {
                    return PlatformSupport.MacPermission(InputMonitoringPermission);
                }
            }
            catch
            {
                // Older macOS without CGPreflightListenEventAccess — continue and check tap result.
            }

            HookStartResult result = HookStartResult.Ok();
            MacRunLoopHost.Shared.RunOnLoop(() =>
            {
                tapCallback = OnTap;
                var mask =
                    MacNative.CGEventMaskBit(MacNative.kCGEventKeyDown) |
                    MacNative.CGEventMaskBit(MacNative.kCGEventKeyUp) |
                    MacNative.CGEventMaskBit(MacNative.kCGEventFlagsChanged) |
                    MacNative.CGEventMaskBit(MacNative.kCGEventLeftMouseDown) |
                    MacNative.CGEventMaskBit(MacNative.kCGEventLeftMouseUp) |
                    MacNative.CGEventMaskBit(MacNative.kCGEventRightMouseDown) |
                    MacNative.CGEventMaskBit(MacNative.kCGEventRightMouseUp) |
                    MacNative.CGEventMaskBit(MacNative.kCGEventOtherMouseDown) |
                    MacNative.CGEventMaskBit(MacNative.kCGEventOtherMouseUp) |
                    MacNative.CGEventMaskBit(MacNative.kCGEventMouseMoved) |
                    MacNative.CGEventMaskBit(MacNative.kCGEventLeftMouseDragged) |
                    MacNative.CGEventMaskBit(MacNative.kCGEventRightMouseDragged) |
                    MacNative.CGEventMaskBit(MacNative.kCGEventOtherMouseDragged) |
                    MacNative.CGEventMaskBit(MacNative.kCGEventScrollWheel);

                tap = MacNative.CGEventTapCreate(
                    MacNative.kCGSessionEventTap,
                    MacNative.kCGHeadInsertEventTap,
                    MacNative.kCGEventTapOptionListenOnly,
                    mask,
                    tapCallback,
                    IntPtr.Zero);

                if (tap == IntPtr.Zero)
                {
                    result = PlatformSupport.MacPermission(InputMonitoringPermission);
                    return;
                }

                runLoopSource = MacNative.CFMachPortCreateRunLoopSource(IntPtr.Zero, tap, 0);
                if (runLoopSource == IntPtr.Zero)
                {
                    MacNative.CFRelease(tap);
                    tap = IntPtr.Zero;
                    result = HookStartResult.Fail(
                        HookFailureReason.NativeFailure,
                        "CFMachPortCreateRunLoopSource failed for CGEventTap.");
                    return;
                }

                MacNative.CFRunLoopAddSource(
                    MacRunLoopHost.Shared.RunLoop,
                    runLoopSource,
                    MacNative.KCFRunLoopCommonModes);
                MacNative.CGEventTapEnable(tap, true);

                if (!MacNative.CGEventTapIsEnabled(tap))
                {
                    TeardownOnLoop();
                    result = PlatformSupport.MacPermission(InputMonitoringPermission);
                }
            });

            return result;
        }

        private void MaybeTeardownUnlocked()
        {
            if (onKey == null && onMouse == null)
            {
                TeardownUnlocked();
            }
        }

        private void TeardownUnlocked()
        {
            if (tap == IntPtr.Zero && runLoopSource == IntPtr.Zero)
            {
                return;
            }

            try
            {
                MacRunLoopHost.Shared.RunOnLoop(TeardownOnLoop);
            }
            catch
            {
                TeardownOnLoop();
            }
        }

        private void TeardownOnLoop()
        {
            if (runLoopSource != IntPtr.Zero)
            {
                try
                {
                    MacNative.CFRunLoopRemoveSource(
                        MacRunLoopHost.Shared.RunLoop,
                        runLoopSource,
                        MacNative.KCFRunLoopCommonModes);
                    MacNative.CFRelease(runLoopSource);
                }
                catch
                {
                    // ignore
                }

                runLoopSource = IntPtr.Zero;
            }

            if (tap != IntPtr.Zero)
            {
                try
                {
                    MacNative.CGEventTapEnable(tap, false);
                    MacNative.CFRelease(tap);
                }
                catch
                {
                    // ignore
                }

                tap = IntPtr.Zero;
            }

            tapCallback = null;
        }

        private IntPtr OnTap(IntPtr proxy, uint type, IntPtr eventRef, IntPtr userInfo)
        {
            _ = proxy;
            _ = userInfo;

            try
            {
                if (type == MacNative.kCGEventKeyDown || type == MacNative.kCGEventKeyUp)
                {
                    var keyHandler = onKey;
                    if (keyHandler != null)
                    {
                        var keyCode = (int)MacNative.CGEventGetIntegerValueField(eventRef, MacNative.kCGKeyboardEventKeycode);
                        var snap = new MacKeySnapshot
                        {
                            MacKeyCode = keyCode,
                            VkCode = MacKeyCodeMap.ToVirtualKey(keyCode),
                            EventType = type == MacNative.kCGEventKeyDown ? 0 : 1,
                            Flags = MacNative.CGEventGetFlags(eventRef)
                        };

                        // Snapshot unicode into the value-type (same class of work as copying key state on Windows).
                        lock (UnicodeScratch)
                        {
                            MacNative.CGEventKeyboardGetUnicodeString(eventRef, 8, out var actual, UnicodeScratch);
                            snap.SetUnicode(UnicodeScratch, (int)actual);
                        }

                        keyHandler(snap);
                    }
                }
                else
                {
                    var mouseHandler = onMouse;
                    if (mouseHandler != null && TryMapMouse(type, eventRef, includeMouseMove, out var mouseSnap))
                    {
                        mouseHandler(mouseSnap);
                    }
                }
            }
            catch
            {
                // never throw from tap
            }

            // Listen-only: return value ignored; pass event through.
            return eventRef;
        }

        private static bool TryMapMouse(uint type, IntPtr eventRef, bool includeMove, out MacMouseSnapshot snapshot)
        {
            snapshot = default;
            var loc = MacNative.CGEventGetLocation(eventRef);
            var point = new Point((int)loc.X, (int)loc.Y);
            uint data = 0;
            MouseMessages message;

            switch (type)
            {
                case MacNative.kCGEventLeftMouseDown:
                    message = MouseMessages.WM_LBUTTONDOWN;
                    break;
                case MacNative.kCGEventLeftMouseUp:
                    message = MouseMessages.WM_LBUTTONUP;
                    break;
                case MacNative.kCGEventRightMouseDown:
                    message = MouseMessages.WM_RBUTTONDOWN;
                    break;
                case MacNative.kCGEventRightMouseUp:
                    message = MouseMessages.WM_RBUTTONUP;
                    break;
                case MacNative.kCGEventOtherMouseDown:
                case MacNative.kCGEventOtherMouseUp:
                    {
                        var button = (int)MacNative.CGEventGetIntegerValueField(eventRef, MacNative.kCGMouseEventButtonNumber);
                        if (button == 2)
                        {
                            message = type == MacNative.kCGEventOtherMouseDown
                                ? MouseMessages.WM_WHEELBUTTONDOWN
                                : MouseMessages.WM_WHEELBUTTONUP;
                        }
                        else
                        {
                            message = type == MacNative.kCGEventOtherMouseDown
                                ? MouseMessages.WM_XBUTTONDOWN
                                : MouseMessages.WM_XBUTTONUP;
                            data = (uint)((button - 2) << 16);
                        }

                        break;
                    }
                case MacNative.kCGEventMouseMoved:
                case MacNative.kCGEventLeftMouseDragged:
                case MacNative.kCGEventRightMouseDragged:
                case MacNative.kCGEventOtherMouseDragged:
                    if (!includeMove)
                    {
                        return false;
                    }

                    message = MouseMessages.WM_MOUSEMOVE;
                    break;
                case MacNative.kCGEventScrollWheel:
                    message = MouseMessages.WM_MOUSEWHEEL;
                    var delta = MacNative.CGEventGetIntegerValueField(eventRef, MacNative.kCGScrollWheelEventDeltaAxis1);
                    data = (uint)((short)(delta * 120) << 16);
                    break;
                default:
                    return false;
            }

            snapshot = new MacMouseSnapshot(message, point, data);
            return true;
        }
    }

    /// <summary>
    /// Shared tap instance so keyboard and mouse watchers share one CGEventTap.
    /// </summary>
    internal static class MacKeyboardMouseHub
    {
        private static readonly object Gate = new object();
        private static MacKeyboardMouse shared;

        internal static MacKeyboardMouse Shared
        {
            get
            {
                lock (Gate)
                {
                    return shared ??= new MacKeyboardMouse();
                }
            }
        }
    }

    /// <summary>
    /// Maps macOS virtual key codes to Win32-style VK values used by <see cref="VirtualKeyNames"/>.
    /// </summary>
    internal static class MacKeyCodeMap
    {
        internal static int ToVirtualKey(int macKeyCode)
        {
            switch (macKeyCode)
            {
                case 0: return 0x41; // A
                case 1: return 0x53; // S
                case 2: return 0x44; // D
                case 3: return 0x46; // F
                case 4: return 0x48; // H
                case 5: return 0x47; // G
                case 6: return 0x5A; // Z
                case 7: return 0x58; // X
                case 8: return 0x43; // C
                case 9: return 0x56; // V
                case 11: return 0x42; // B
                case 12: return 0x51; // Q
                case 13: return 0x57; // W
                case 14: return 0x45; // E
                case 15: return 0x52; // R
                case 16: return 0x59; // Y
                case 17: return 0x54; // T
                case 18: return 0x31; // 1
                case 19: return 0x32; // 2
                case 20: return 0x33; // 3
                case 21: return 0x34; // 4
                case 22: return 0x36; // 6
                case 23: return 0x35; // 5
                case 25: return 0x39; // 9
                case 26: return 0x37; // 7
                case 28: return 0x38; // 8
                case 29: return 0x30; // 0
                case 36: return 0x0D; // Return
                case 48: return 0x09; // Tab
                case 49: return 0x20; // Space
                case 51: return 0x08; // Delete (Backspace)
                case 53: return 0x1B; // Escape
                case 55: return 0x5B; // Command → LWin
                case 56: return 0xA0; // Shift
                case 57: return 0x14; // CapsLock
                case 58: return 0xA4; // Option → LeftAlt
                case 59: return 0xA2; // Control
                case 60: return 0xA1; // RightShift
                case 61: return 0xA5; // RightOption
                case 62: return 0xA3; // RightControl
                case 63: return 0x5C; // Function / right meta-ish
                case 96: return 0x75; // F5
                case 97: return 0x76; // F6
                case 98: return 0x77; // F7
                case 99: return 0x73; // F3
                case 100: return 0x78; // F8
                case 101: return 0x79; // F9
                case 103: return 0x7A; // F11
                case 105: return 0x7B; // F12 (approx)
                case 109: return 0x7A; // F10
                case 111: return 0x7B; // F12
                case 118: return 0x71; // F2
                case 120: return 0x70; // F1
                case 122: return 0x72; // F4
                case 123: return 0x25; // Left
                case 124: return 0x27; // Right
                case 125: return 0x28; // Down
                case 126: return 0x26; // Up
                case 115: return 0x24; // Home
                case 119: return 0x23; // End
                case 116: return 0x21; // PageUp
                case 121: return 0x22; // PageDown
                case 117: return 0x2E; // Forward Delete
                default: return 0x1000 + macKeyCode;
            }
        }

        /// <summary>
        /// Maps portable <see cref="EventKey"/> to macOS virtual key code for Carbon hotkeys.
        /// </summary>
        internal static bool TryToMacKeyCode(EventKey key, out uint macKeyCode)
        {
            switch (key)
            {
                case EventKey.A: macKeyCode = 0; return true;
                case EventKey.S: macKeyCode = 1; return true;
                case EventKey.D: macKeyCode = 2; return true;
                case EventKey.F: macKeyCode = 3; return true;
                case EventKey.H: macKeyCode = 4; return true;
                case EventKey.G: macKeyCode = 5; return true;
                case EventKey.Z: macKeyCode = 6; return true;
                case EventKey.X: macKeyCode = 7; return true;
                case EventKey.C: macKeyCode = 8; return true;
                case EventKey.V: macKeyCode = 9; return true;
                case EventKey.B: macKeyCode = 11; return true;
                case EventKey.Q: macKeyCode = 12; return true;
                case EventKey.W: macKeyCode = 13; return true;
                case EventKey.E: macKeyCode = 14; return true;
                case EventKey.R: macKeyCode = 15; return true;
                case EventKey.Y: macKeyCode = 16; return true;
                case EventKey.T: macKeyCode = 17; return true;
                case EventKey.D1: macKeyCode = 18; return true;
                case EventKey.D2: macKeyCode = 19; return true;
                case EventKey.D3: macKeyCode = 20; return true;
                case EventKey.D4: macKeyCode = 21; return true;
                case EventKey.D6: macKeyCode = 22; return true;
                case EventKey.D5: macKeyCode = 23; return true;
                case EventKey.D9: macKeyCode = 25; return true;
                case EventKey.D7: macKeyCode = 26; return true;
                case EventKey.D8: macKeyCode = 28; return true;
                case EventKey.D0: macKeyCode = 29; return true;
                case EventKey.Return: macKeyCode = 36; return true;
                case EventKey.Tab: macKeyCode = 48; return true;
                case EventKey.Space: macKeyCode = 49; return true;
                case EventKey.Back: macKeyCode = 51; return true;
                case EventKey.Escape: macKeyCode = 53; return true;
                case EventKey.F5: macKeyCode = 96; return true;
                case EventKey.F6: macKeyCode = 97; return true;
                case EventKey.F7: macKeyCode = 98; return true;
                case EventKey.F3: macKeyCode = 99; return true;
                case EventKey.F8: macKeyCode = 100; return true;
                case EventKey.F9: macKeyCode = 101; return true;
                case EventKey.F11: macKeyCode = 103; return true;
                case EventKey.F10: macKeyCode = 109; return true;
                case EventKey.F12: macKeyCode = 111; return true;
                case EventKey.F2: macKeyCode = 118; return true;
                case EventKey.F1: macKeyCode = 120; return true;
                case EventKey.F4: macKeyCode = 122; return true;
                case EventKey.Left: macKeyCode = 123; return true;
                case EventKey.Right: macKeyCode = 124; return true;
                case EventKey.Down: macKeyCode = 125; return true;
                case EventKey.Up: macKeyCode = 126; return true;
                case EventKey.Home: macKeyCode = 115; return true;
                case EventKey.End: macKeyCode = 119; return true;
                case EventKey.PageUp: macKeyCode = 116; return true;
                case EventKey.PageDown: macKeyCode = 121; return true;
                case EventKey.Delete: macKeyCode = 117; return true;
                default:
                    macKeyCode = 0;
                    return false;
            }
        }
    }
}
