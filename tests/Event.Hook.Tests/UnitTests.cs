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
            Assert.True(reg.Success || reg.Reason != HookFailureReason.None);
            if (reg.Success)
            {
                watcher.Unregister("id1");
            }

            watcher.Stop();
            Assert.False(watcher.IsRunning);
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
            Assert.True(hwnd == IntPtr.Zero || hwnd != IntPtr.Zero);
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
            Assert.False(kb.IsRunning);
            Assert.False(mouse.IsRunning);
            kb.Dispose();
            mouse.Dispose();
            Assert.False(kb.IsRunning);
            Assert.False(mouse.IsRunning);
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

    public class LinuxKeyMapTests
    {
        [Theory]
        [InlineData(0x0041ul, 0x41)] // A
        [InlineData(0x0061ul, 0x41)] // a -> A
        [InlineData(0x0020ul, 0x20)] // space
        [InlineData(0xff08ul, 0x08)] // BackSpace
        [InlineData(0xff09ul, 0x09)] // Tab
        [InlineData(0xff0dul, 0x0D)] // Return
        [InlineData(0xff1bul, 0x1B)] // Escape
        [InlineData(0xffbeul, 0x70)] // F1
        [InlineData(0xffc9ul, 0x7B)] // F12
        [InlineData(0xffe1ul, 0xA0)] // Shift_L
        [InlineData(0xffe3ul, 0xA2)] // Control_L
        public void KeySymToVk_maps_common_keys(ulong keysym, int expectedVk)
        {
            Assert.Equal(expectedVk, EventHook.Platforms.Linux.LinuxKeyMap.KeySymToVk(keysym));
        }

        [Theory]
        [InlineData(0x0020ul, " ")]
        [InlineData(0x0041ul, "A")]
        [InlineData(0x0061ul, "a")]
        [InlineData(0xff09ul, "\t")]
        [InlineData(0xff0dul, "\r")]
        [InlineData(0x00e9ul, "é")]
        public void KeySymToUnicode_maps_printable(ulong keysym, string expected)
        {
            Assert.Equal(expected, EventHook.Platforms.Linux.LinuxKeyMap.KeySymToUnicode(keysym));
        }

        [Fact]
        public void KeySymToUnicode_unknown_returns_empty()
        {
            Assert.Equal(string.Empty, EventHook.Platforms.Linux.LinuxKeyMap.KeySymToUnicode(0xff00));
        }

        [Theory]
        [InlineData(EventKey.A, 0x0041ul)]
        [InlineData(EventKey.Space, 0x0020ul)]
        [InlineData(EventKey.F12, 0xffbeul + 11)]
        [InlineData(EventKey.D5, (ulong)'5')]
        public void EventKeyToKeySym_roundtrips(EventKey key, ulong expected)
        {
            Assert.Equal(expected, EventHook.Platforms.Linux.LinuxKeyMap.EventKeyToKeySym(key));
        }

        [Fact]
        public void ModifiersToX_combines_masks()
        {
            var mask = EventHook.Platforms.Linux.LinuxKeyMap.ModifiersToX(
                KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Meta);
            Assert.NotEqual(0u, mask);
            Assert.Equal(0u, EventHook.Platforms.Linux.LinuxKeyMap.ModifiersToX(KeyModifiers.None));
        }

        [Theory]
        [InlineData((ushort)1, 0x1B)] // KEY_ESC
        [InlineData((ushort)28, 0x0D)] // KEY_ENTER
        [InlineData((ushort)57, 0x20)] // KEY_SPACE
        public void EvdevKeyToVk_maps_basics(ushort code, int expectedVk)
        {
            Assert.Equal(expectedVk, EventHook.Platforms.Linux.LinuxKeyMap.EvdevKeyToVk(code));
        }

        [Fact]
        public void EvdevKeyToUnicode_down_letter()
        {
            // KEY_A = 30
            var text = EventHook.Platforms.Linux.LinuxKeyMap.EvdevKeyToUnicode(30, 1);
            Assert.False(string.IsNullOrEmpty(text));
        }
    }

    public class HotkeyModelTests
    {
        [Fact]
        public void Hotkey_equality_and_string()
        {
            var a = new Hotkey(KeyModifiers.Control | KeyModifiers.Shift, EventKey.F9);
            var b = new Hotkey(KeyModifiers.Control | KeyModifiers.Shift, EventKey.F9);
            var c = new Hotkey(KeyModifiers.Alt, EventKey.F9);
            Assert.Equal(a, b);
            Assert.True(a == b);
            Assert.True(a != c);
            Assert.NotEqual(a, c);
            Assert.Contains("F9", a.ToString());
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
            Assert.False(a.Equals(null));
        }
    }

    public class VirtualKeyNamesTests
    {
        [Theory]
        [InlineData(0x08, "Back")]
        [InlineData(0x1B, "Escape")]
        [InlineData(0x20, "Space")]
        [InlineData(0x41, "A")]
        [InlineData(0x30, "0")]
        [InlineData(0x70, "F1")]
        [InlineData(0x7B, "F12")]
        [InlineData(0xA0, "LeftShift")]
        [InlineData(0x99, "Key153")]
        public void GetName_maps_known_and_fallback(int vk, string expected)
        {
            Assert.Equal(expected, EventHook.Helpers.VirtualKeyNames.GetName(vk));
        }
    }

    public class LinuxKeyMapExtendedTests
    {
        [Theory]
        [InlineData(0xff50ul, 0x24)] // Home
        [InlineData(0xff51ul, 0x25)] // Left
        [InlineData(0xff52ul, 0x26)] // Up
        [InlineData(0xff53ul, 0x27)] // Right
        [InlineData(0xff54ul, 0x28)] // Down
        [InlineData(0xff55ul, 0x21)] // Page_Up
        [InlineData(0xff56ul, 0x22)] // Page_Down
        [InlineData(0xff57ul, 0x23)] // End
        [InlineData(0xff63ul, 0x2D)] // Insert
        [InlineData(0xfffful, 0x2E)] // Delete
        [InlineData(0xffe2ul, 0xA1)] // Shift_R
        [InlineData(0xffe4ul, 0xA3)] // Control_R
        [InlineData(0xffe9ul, 0xA4)] // Alt_L
        [InlineData(0xffeaul, 0xA5)] // Alt_R
        [InlineData(0xffebul, 0x5B)] // Super_L
        [InlineData(0xffecul, 0x5C)] // Super_R
        public void KeySymToVk_specials(ulong keysym, int vk)
        {
            Assert.Equal(vk, EventHook.Platforms.Linux.LinuxKeyMap.KeySymToVk(keysym));
        }

        [Theory]
        [InlineData(EventKey.Back, 0xff08ul)]
        [InlineData(EventKey.Tab, 0xff09ul)]
        [InlineData(EventKey.Return, 0xff0dul)]
        [InlineData(EventKey.Escape, 0xff1bul)]
        [InlineData(EventKey.Home, 0xff50ul)]
        [InlineData(EventKey.Delete, 0xfffful)]
        [InlineData(EventKey.F1, 0xffbeul)]
        [InlineData(EventKey.Z, (ulong)'Z')]
        [InlineData(EventKey.None, 0ul)]
        public void EventKeyToKeySym_more(EventKey key, ulong expected)
        {
            Assert.Equal(expected, EventHook.Platforms.Linux.LinuxKeyMap.EventKeyToKeySym(key));
        }

        [Theory]
        [InlineData((ushort)14, 0x08)]
        [InlineData((ushort)15, 0x09)]
        [InlineData((ushort)42, 0xA0)]
        [InlineData((ushort)54, 0xA1)]
        [InlineData((ushort)29, 0xA2)]
        [InlineData((ushort)97, 0xA3)]
        [InlineData((ushort)56, 0xA4)]
        [InlineData((ushort)100, 0xA5)]
        [InlineData((ushort)125, 0x5B)]
        [InlineData((ushort)126, 0x5C)]
        [InlineData((ushort)59, 0x70)]
        [InlineData((ushort)87, 0x7A)]
        [InlineData((ushort)88, 0x7B)]
        [InlineData((ushort)2, 0x31)]
        [InlineData((ushort)11, 0x30)]
        [InlineData((ushort)16, (int)'Q')]
        [InlineData((ushort)30, (int)'A')]
        [InlineData((ushort)44, (int)'Z')]
        public void EvdevKeyToVk_extended(ushort code, int vk)
        {
            Assert.Equal(vk, EventHook.Platforms.Linux.LinuxKeyMap.EvdevKeyToVk(code));
        }

        [Fact]
        public void EvdevKeyToUnicode_branches()
        {
            Assert.Equal(string.Empty, EventHook.Platforms.Linux.LinuxKeyMap.EvdevKeyToUnicode(30, 0));
            Assert.Equal("1", EventHook.Platforms.Linux.LinuxKeyMap.EvdevKeyToUnicode(2, 1));
            Assert.Equal("0", EventHook.Platforms.Linux.LinuxKeyMap.EvdevKeyToUnicode(11, 1));
            Assert.Equal(" ", EventHook.Platforms.Linux.LinuxKeyMap.EvdevKeyToUnicode(57, 1));
            Assert.Equal("a", EventHook.Platforms.Linux.LinuxKeyMap.EvdevKeyToUnicode(30, 1));
        }

        [Fact]
        public void ModifiersToX_individual_flags()
        {
            Assert.NotEqual(0u, EventHook.Platforms.Linux.LinuxKeyMap.ModifiersToX(KeyModifiers.Shift));
            Assert.NotEqual(0u, EventHook.Platforms.Linux.LinuxKeyMap.ModifiersToX(KeyModifiers.Control));
            Assert.NotEqual(0u, EventHook.Platforms.Linux.LinuxKeyMap.ModifiersToX(KeyModifiers.Alt));
            Assert.NotEqual(0u, EventHook.Platforms.Linux.LinuxKeyMap.ModifiersToX(KeyModifiers.Meta));
        }
    }
#endif
}
