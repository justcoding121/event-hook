using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using EventHook.Helpers;
using EventHook.Hooks;
using Xunit;

#if WINDOWS
using System.Windows.Forms;
using EventHook.Hooks.Library;
#endif

namespace EventHook.Tests
{
    public class MouseMessageFilterTests
    {
        [Fact]
        public void Excludes_mouse_move_when_disabled()
        {
            Assert.False(MouseMessageFilter.ShouldRaise(MouseMessages.WM_MOUSEMOVE, false));
            Assert.True(MouseMessageFilter.ShouldRaise(MouseMessages.WM_LBUTTONDOWN, false));
            Assert.True(MouseMessageFilter.ShouldRaise(MouseMessages.WM_MOUSEMOVE, true));
        }
    }

    public class EventOffloadTests
    {
        [Fact]
        public async Task TryWrite_and_ReadAsync_roundtrip()
        {
            using var offload = new EventOffload<int>(capacity: 8);
            Assert.True(offload.TryWrite(1));
            Assert.True(offload.TryWrite(2));
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            Assert.Equal(1, await offload.ReadAsync(cts.Token));
            Assert.Equal(2, await offload.ReadAsync(cts.Token));
        }

        [Fact]
        public void TryWrite_when_full_increments_dropped()
        {
            using var offload = new EventOffload<int>(capacity: 1);
            var wrote = 0;
            for (var i = 0; i < 32; i++)
            {
                if (offload.TryWrite(i))
                {
                    wrote++;
                }
            }

            Assert.True(wrote >= 1);
            Assert.True(offload.DroppedEventCount >= 1);
        }
    }

    public class HookStartResultTests
    {
        [Fact]
        public void Ok_and_Fail_and_ThrowIfFailed()
        {
            Assert.True(HookStartResult.Ok().Success);
            var fail = HookStartResult.Fail(HookFailureReason.PermissionDenied, "denied");
            Assert.False(fail.Success);
            Assert.Equal(HookFailureReason.PermissionDenied, fail.Reason);
            var ex = Assert.Throws<EventHookException>(() => fail.ThrowIfFailed());
            Assert.Equal(HookFailureReason.PermissionDenied, ex.Reason);
            HookStartResult.Ok().ThrowIfFailed();
        }
    }

    public class HotkeyRegisterTests
    {
        [Fact]
        public void Register_and_unregister_bookkeeping_does_not_throw_when_stopped()
        {
            using var factory = new EventHookFactory();
            using var watcher = factory.GetHotkeyWatcher();
            var start = watcher.Start();
            if (!start.Success)
            {
                Assert.NotEqual(HookFailureReason.None, start.Reason);
                return;
            }

            var reg = watcher.Register("id1", new Hotkey(KeyModifiers.Control | KeyModifiers.Shift, EventKey.F9));
            if (reg.Success)
            {
                watcher.Unregister("id1");
            }

            watcher.Stop();
        }
    }

#if WINDOWS
    public class ClipboardClassifyTests
    {
        [Fact]
        public void Classifies_unicode_text()
        {
            var data = new DataObject();
            data.SetText("hello", TextDataFormat.UnicodeText);
            Assert.True(ClipboardWatcher.TryClassify(data, out var format, out var value));
            Assert.Equal(ClipboardContentTypes.UnicodeText, format);
            Assert.Equal("hello", value);
        }

        [Fact]
        public void Classifies_file_drop()
        {
            var data = new DataObject();
            data.SetData(DataFormats.FileDrop, new[] { @"C:\temp\a.txt" });
            Assert.True(ClipboardWatcher.TryClassify(data, out var format, out var value));
            Assert.Equal(ClipboardContentTypes.FileDrop, format);
            Assert.NotNull(value);
        }
    }

    public class PInvokeSignatureTests
    {
        [Fact]
        public void Print_notify_struct_size_is_positive()
        {
            Assert.True(Marshal.SizeOf<PRINTER_NOTIFY_INFO_DATA>() > 0);
            Assert.True(Marshal.SizeOf<PRINTER_NOTIFY_INFO>() > 0);
        }

        [Fact]
        public void GetForegroundWindow_returns_IntPtr()
        {
            var hwnd = User32.GetForegroundWindow();
            _ = hwnd;
        }
    }
#endif

    public class LifecycleTests
    {
        [Fact]
        public void Dispose_without_start_is_safe()
        {
            using var factory = new EventHookFactory();
            using var kb = factory.GetKeyboardWatcher();
            using var mouse = factory.GetMouseWatcher();
            kb.Dispose();
            mouse.Dispose();
        }

        [Fact]
        public void Double_stop_is_safe()
        {
            using var factory = new EventHookFactory();
            var mouse = factory.GetMouseWatcher();
            mouse.IncludeMouseMove = false;
            var result = mouse.Start();
            if (result.Success)
            {
                Assert.True(mouse.IsRunning);
            }
            else
            {
                Assert.False(mouse.IsRunning);
            }

            mouse.Stop();
            mouse.Stop();
        }

        [Fact]
        public void Failed_start_does_not_set_IsRunning()
        {
            // On portable TFM under Windows host, Start returns WindowsOnlyTfm.
            // On unix stubs before backends, NotSupportedYet.
            // Either way Success false => IsRunning false.
            if (OperatingSystem.IsWindows() && !PlatformSupport.IsWindowsTfm)
            {
                using var factory = new EventHookFactory();
                var kb = factory.GetKeyboardWatcher();
                var result = kb.Start();
                Assert.False(result.Success);
                Assert.False(kb.IsRunning);
                Assert.Equal(HookFailureReason.NotSupportedOnPlatform, result.Reason);
            }
        }
    }

    public class PlatformSupportMessageTests
    {
        [Fact]
        public void WaylandNeedsX11_message_names_feature_and_DISPLAY()
        {
            var result = PlatformSupport.WaylandNeedsX11("Clipboard");
            Assert.False(result.Success);
            Assert.Equal(HookFailureReason.NotSupportedOnPlatform, result.Reason);
            Assert.Contains("Clipboard", result.Message);
            Assert.Contains("DISPLAY", result.Message);
        }

        [Fact]
        public void PrivilegeInputGroup_mentions_input_group()
        {
            var result = PlatformSupport.PrivilegeInputGroup();
            Assert.False(result.Success);
            Assert.Equal(HookFailureReason.PrivilegeRequired, result.Reason);
            Assert.Contains("input", result.Message);
            Assert.Contains("/dev/input", result.Message);
        }

        [Fact]
        public void DisplayUnavailable_mentions_session_env()
        {
            var result = PlatformSupport.DisplayUnavailable();
            Assert.False(result.Success);
            Assert.Equal(HookFailureReason.DisplayUnavailable, result.Reason);
            Assert.Contains("DISPLAY", result.Message);
        }
    }

#if !WINDOWS
    public class MacPlatformMessageTests
    {
        [Fact]
        public void MacPermission_mentions_settings_and_service()
        {
            var result = PlatformSupport.MacPermission("Input Monitoring");
            Assert.False(result.Success);
            Assert.Equal(HookFailureReason.PermissionDenied, result.Reason);
            Assert.Contains("Input Monitoring", result.Message);
            Assert.Contains("Privacy & Security", result.Message);
        }

        [Fact]
        public void MacKeyCodeMap_roundtrips_letter_A()
        {
            Assert.True(EventHook.Platforms.Mac.MacKeyCodeMap.TryToMacKeyCode(EventKey.A, out var mac));
            Assert.Equal(0u, mac);
            Assert.Equal(0x41, EventHook.Platforms.Mac.MacKeyCodeMap.ToVirtualKey(0));
        }

        [Fact]
        public void MacKeyCodeMap_maps_F12_hotkey()
        {
            Assert.True(EventHook.Platforms.Mac.MacKeyCodeMap.TryToMacKeyCode(EventKey.F12, out var mac));
            Assert.True(mac > 0);
        }
    }

    public class LinuxSessionTests
    {
        [Fact]
        public void RequireX11_ok_when_DISPLAY_set()
        {
            var previous = Environment.GetEnvironmentVariable("DISPLAY");
            var previousWayland = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
            try
            {
                Environment.SetEnvironmentVariable("DISPLAY", ":99");
                Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", null);
                Assert.True(EventHook.Platforms.Linux.LinuxSession.HasX11Display);
                var result = EventHook.Platforms.Linux.LinuxSession.RequireX11("Clipboard");
                Assert.True(result.Success);
            }
            finally
            {
                Environment.SetEnvironmentVariable("DISPLAY", previous);
                Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", previousWayland);
            }
        }

        [Fact]
        public void RequireX11_wayland_only_returns_WaylandNeedsX11()
        {
            var previous = Environment.GetEnvironmentVariable("DISPLAY");
            var previousWayland = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
            try
            {
                Environment.SetEnvironmentVariable("DISPLAY", null);
                Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", "wayland-0");
                Assert.True(EventHook.Platforms.Linux.LinuxSession.IsWaylandOnly);
                var result = EventHook.Platforms.Linux.LinuxSession.RequireX11("Hotkey");
                Assert.False(result.Success);
                Assert.Equal(HookFailureReason.NotSupportedOnPlatform, result.Reason);
                Assert.Contains("Hotkey", result.Message);
                Assert.Contains("DISPLAY", result.Message);
            }
            finally
            {
                Environment.SetEnvironmentVariable("DISPLAY", previous);
                Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", previousWayland);
            }
        }

        [Fact]
        public void RequireX11_no_session_returns_DisplayUnavailable()
        {
            var previous = Environment.GetEnvironmentVariable("DISPLAY");
            var previousWayland = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
            try
            {
                Environment.SetEnvironmentVariable("DISPLAY", null);
                Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", null);
                var result = EventHook.Platforms.Linux.LinuxSession.RequireX11("Application");
                Assert.False(result.Success);
                Assert.Equal(HookFailureReason.DisplayUnavailable, result.Reason);
            }
            finally
            {
                Environment.SetEnvironmentVariable("DISPLAY", previous);
                Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", previousWayland);
            }
        }
    }
#endif
}
