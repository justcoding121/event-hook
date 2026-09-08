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
}
