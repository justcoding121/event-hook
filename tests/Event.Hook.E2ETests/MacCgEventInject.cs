#if !WINDOWS
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace EventHook.E2ETests
{
    /// <summary>
    /// Posts Quartz HID events and drives a sacrificial app so macOS e2e can
    /// verify CGEventTap, Carbon hotkeys, clipboard, and application delivery.
    /// </summary>
    internal static class MacCgEventInject
    {
        private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
        private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        private const string ApplicationServices = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

        private const uint kCGHIDEventTap = 0;
        private const uint kCGEventLeftMouseDown = 1;
        private const uint kCGEventLeftMouseUp = 2;
        private const uint kCGMouseButtonLeft = 0;
        private const uint kCGScrollEventUnitLine = 0;

        private const ulong kCGEventFlagMaskShift = 0x00020000;
        private const ulong kCGEventFlagMaskControl = 0x00040000;
        private const ulong kCGEventFlagMaskAlternate = 0x00080000;

        private const ushort KeyA = 0;
        private const ushort KeyF11 = 103;
        private const ushort KeyShift = 56;
        private const ushort KeyOption = 58;
        private const ushort KeyControl = 59;

        internal static bool IsMacOS => OperatingSystem.IsMacOS();

        internal static bool TryFocusTextEdit()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = "-a TextEdit",
                    UseShellExecute = false
                })?.WaitForExit(5000);

                return RunOsascript("tell application \"TextEdit\" to activate") == 0;
            }
            catch
            {
                return false;
            }
        }

        internal static bool TryQuitTextEdit()
        {
            return RunOsascript("tell application \"TextEdit\" to quit") == 0;
        }

        internal static bool TrySetTextEditDocumentName(string name)
        {
            var script =
                "tell application \"TextEdit\"\n" +
                "activate\n" +
                "if (count of documents) is 0 then make new document\n" +
                "set name of front document to \"" + name.Replace("\"", string.Empty) + "\"\n" +
                "end tell";
            return RunOsascript(script) == 0;
        }

        internal static bool TryMinimizeTextEdit()
        {
            return RunOsascript(
                "tell application \"System Events\" to tell process \"TextEdit\" to set value of attribute \"AXMinimized\" of window 1 to true") == 0;
        }

        internal static bool TryFakeKeyA()
        {
            return PostKey(KeyA, flags: 0);
        }

        internal static bool TryFakeLeftClick()
        {
            if (!TryGetTextEditClickPoint(out var x, out var y))
            {
                x = 120;
                y = 160;
            }

            var point = new CGPoint { X = x, Y = y };
            var down = CGEventCreateMouseEvent(IntPtr.Zero, kCGEventLeftMouseDown, point, kCGMouseButtonLeft);
            var up = CGEventCreateMouseEvent(IntPtr.Zero, kCGEventLeftMouseUp, point, kCGMouseButtonLeft);
            if (down == IntPtr.Zero || up == IntPtr.Zero)
            {
                Release(down);
                Release(up);
                return false;
            }

            CGEventPost(kCGHIDEventTap, down);
            CGEventPost(kCGHIDEventTap, up);
            Release(down);
            Release(up);
            return true;
        }

        internal static bool TryFakeScroll()
        {
            var ev = CGEventCreateScrollWheelEvent(IntPtr.Zero, kCGScrollEventUnitLine, 1, 3);
            if (ev == IntPtr.Zero)
            {
                return false;
            }

            CGEventPost(kCGHIDEventTap, ev);
            Release(ev);
            return true;
        }

        internal static bool TryFakeHotkeyCtrlAltShiftF11()
        {
            const ulong flags = kCGEventFlagMaskControl | kCGEventFlagMaskAlternate | kCGEventFlagMaskShift;
            var source = CGEventSourceCreate(1);
            ushort[] downs = { KeyControl, KeyOption, KeyShift, KeyF11 };
            ushort[] ups = { KeyF11, KeyShift, KeyOption, KeyControl };
            foreach (var key in downs)
            {
                var ev = CGEventCreateKeyboardEvent(source, key, true);
                if (ev == IntPtr.Zero)
                {
                    return false;
                }

                CGEventSetFlags(ev, flags);
                CGEventPost(kCGHIDEventTap, ev);
                Release(ev);
            }

            foreach (var key in ups)
            {
                var ev = CGEventCreateKeyboardEvent(source, key, false);
                if (ev == IntPtr.Zero)
                {
                    return false;
                }

                CGEventSetFlags(ev, key == KeyF11 ? flags : 0);
                CGEventPost(kCGHIDEventTap, ev);
                Release(ev);
            }

            return true;
        }

        internal static bool TryCopyText(string text)
        {
            try
            {
                var pb = Process.Start(new ProcessStartInfo
                {
                    FileName = "pbcopy",
                    RedirectStandardInput = true,
                    UseShellExecute = false
                });
                if (pb == null)
                {
                    return false;
                }

                pb.StandardInput.Write(text);
                pb.StandardInput.Close();
                pb.WaitForExit(3000);
                return pb.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        internal static bool TryLaunchStickies()
        {
            try
            {
                var open = Process.Start(new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = "-n -a Stickies",
                    UseShellExecute = false
                });
                open?.WaitForExit(5000);
                return open?.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        internal static void TryQuitStickies()
        {
            RunOsascript("tell application \"Stickies\" to quit");
        }

        [DllImport(CoreGraphics)]
        private static extern IntPtr CGEventSourceCreate(uint stateID);

        private static bool PostKey(ushort keyCode, ulong flags)
        {
            var source = CGEventSourceCreate(1);
            var down = CGEventCreateKeyboardEvent(source, keyCode, true);
            var up = CGEventCreateKeyboardEvent(source, keyCode, false);
            if (down == IntPtr.Zero || up == IntPtr.Zero)
            {
                Release(down);
                Release(up);
                return false;
            }

            if (flags != 0)
            {
                CGEventSetFlags(down, flags);
                CGEventSetFlags(up, flags);
            }

            CGEventPost(kCGHIDEventTap, down);
            CGEventPost(kCGHIDEventTap, up);
            Release(down);
            Release(up);
            return true;
        }

        private static bool TryGetTextEditClickPoint(out double x, out double y)
        {
            x = 0;
            y = 0;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "osascript",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                psi.ArgumentList.Add("-e");
                psi.ArgumentList.Add(
                    "tell application \"System Events\" to tell process \"TextEdit\" to get {position, size} of window 1");
                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    return false;
                }

                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(3000);
                var parts = output.Split(new[] { ',', '\n', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 4)
                {
                    return false;
                }

                if (!double.TryParse(parts[0], out var left) ||
                    !double.TryParse(parts[1], out var top) ||
                    !double.TryParse(parts[2], out var width) ||
                    !double.TryParse(parts[3], out var height))
                {
                    return false;
                }

                x = left + Math.Max(40, width / 2);
                y = top + Math.Max(40, height / 2);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static int RunOsascript(string script)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "osascript",
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            psi.ArgumentList.Add("-e");
            psi.ArgumentList.Add(script);
            using var proc = Process.Start(psi);
            if (proc == null)
            {
                return -1;
            }

            if (!proc.WaitForExit(5000))
            {
                try
                {
                    proc.Kill(entireProcessTree: true);
                }
                catch
                {
                    // best-effort
                }

                return -1;
            }

            return proc.ExitCode;
        }

        private static void Release(IntPtr cf)
        {
            if (cf != IntPtr.Zero)
            {
                CFRelease(cf);
            }
        }

        [DllImport(CoreGraphics)]
        private static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort virtualKey, bool keyDown);

        [DllImport(CoreGraphics)]
        private static extern IntPtr CGEventCreateMouseEvent(
            IntPtr source, uint mouseType, CGPoint mouseCursorPosition, uint mouseButton);

        [DllImport(CoreGraphics)]
        private static extern IntPtr CGEventCreateScrollWheelEvent(
            IntPtr source, uint units, uint wheelCount, int wheel1);

        [DllImport(CoreGraphics)]
        private static extern void CGEventSetFlags(IntPtr cgEvent, ulong flags);

        [DllImport(CoreGraphics)]
        private static extern void CGEventPost(uint tap, IntPtr cgEvent);

        [DllImport(CoreFoundation)]
        private static extern void CFRelease(IntPtr cf);

        [DllImport(ApplicationServices)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool AXIsProcessTrusted();

        [StructLayout(LayoutKind.Sequential)]
        private struct CGPoint
        {
            public double X;
            public double Y;
        }
    }
}
#endif
