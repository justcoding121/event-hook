using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using EventHook.Helpers;
using EventHook.Hooks;
using EventHook.Hooks.Library;
using Xunit;

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

    public class AsyncConcurrentQueueTests
    {
        [Fact]
        public async Task Dequeues_enqueued_items()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var q = new AsyncConcurrentQueue<string>(cts.Token);
            q.Enqueue("a");
            q.Enqueue("b");
            Assert.Equal("a", await q.DequeueAsync());
            Assert.Equal("b", await q.DequeueAsync());
        }
    }

    public class HotkeySplitTests
    {
        [Fact]
        public void Register_and_unregister_bookkeeping_does_not_throw_when_stopped()
        {
            using var factory = new EventHookFactory();
            using var watcher = factory.GetHotkeyWatcher();
            watcher.Start();
            watcher.Register("id1", Keys.Control | Keys.Shift | Keys.F9);
            watcher.Unregister("id1");
            watcher.Stop();
        }
    }

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
            // Zero is valid when nothing is focused; type correctness is the goal.
            _ = hwnd;
        }
    }

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
            mouse.Start();
            mouse.Stop();
            mouse.Stop();
        }
    }
}
