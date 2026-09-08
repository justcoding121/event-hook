using System;
using System.Threading;
using Xunit;

namespace EventHook.IntegrationTests
{
    public class LifecycleIntegrationTests
    {
        private static void AssertStartOkOrExpected(HookStartResult result)
        {
            if (result.Success)
            {
                return;
            }

            Assert.Contains(result.Reason, new[]
            {
                HookFailureReason.PermissionDenied,
                HookFailureReason.PrivilegeRequired,
                HookFailureReason.DisplayUnavailable,
                HookFailureReason.NotSupportedOnPlatform,
                HookFailureReason.NativeFailure,
                HookFailureReason.AlreadyInUse
            });
            Assert.False(string.IsNullOrWhiteSpace(result.Message));
        }

        [Fact]
        [Trait("Category", "Integration")]
        public void Factory_start_stop_keyboard_and_mouse_exits_cleanly()
        {
            using var factory = new EventHookFactory();
            var kb = factory.GetKeyboardWatcher();
            var mouse = factory.GetMouseWatcher();
            mouse.IncludeMouseMove = false;

            var kbResult = kb.Start();
            var mouseResult = mouse.Start();
            AssertStartOkOrExpected(kbResult);
            AssertStartOkOrExpected(mouseResult);
            Thread.Sleep(200);
            kb.Stop();
            mouse.Stop();
        }

        [Fact]
        [Trait("Category", "Integration")]
        public void HotkeyWatcher_registers_and_unregisters()
        {
            using var factory = new EventHookFactory();
            var hotkeys = factory.GetHotkeyWatcher();
            var start = hotkeys.Start();
            AssertStartOkOrExpected(start);
            if (!start.Success)
            {
                return;
            }

            var result = hotkeys.Register("it",
                new Hotkey(KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift, EventKey.F12));
            if (!result.Success)
            {
                Assert.Equal(HookFailureReason.AlreadyInUse, result.Reason);
                hotkeys.Stop();
                return;
            }

            Thread.Sleep(100);
            hotkeys.Unregister("it");
            hotkeys.Stop();
        }

        [Fact]
        [Trait("Category", "Integration")]
        public void ClipboardWatcher_start_stop()
        {
            using var factory = new EventHookFactory();
            var clip = factory.GetClipboardWatcher();
            AssertStartOkOrExpected(clip.Start());
            Thread.Sleep(200);
            clip.Stop();
        }

#if WINDOWS
        [Fact]
        [Trait("Category", "Integration")]
        public void ClipboardWatcher_sees_text_change()
        {
            if (!Environment.UserInteractive)
            {
                return;
            }

            using var factory = new EventHookFactory();
            var clip = factory.GetClipboardWatcher();
            var saw = new ManualResetEventSlim(false);
            clip.OnClipboardModified += (_, e) =>
            {
                if (e.DataFormat == ClipboardContentTypes.UnicodeText ||
                    e.DataFormat == ClipboardContentTypes.PlainText)
                {
                    saw.Set();
                }
            };
            var start = clip.Start();
            AssertStartOkOrExpected(start);
            if (!start.Success)
            {
                return;
            }

            Exception staError = null;
            var sta = new Thread(() =>
            {
                try
                {
                    System.Windows.Forms.Clipboard.SetText("eventhook-it-" + Guid.NewGuid());
                }
                catch (Exception ex)
                {
                    staError = ex;
                }
            });
            sta.SetApartmentState(ApartmentState.STA);
            sta.Start();
            sta.Join();

            if (staError != null)
            {
                clip.Stop();
                return;
            }

            saw.Wait(TimeSpan.FromSeconds(5));
            clip.Stop();
        }
#endif

        [Fact]
        [Trait("Category", "Integration")]
        public void PrintWatcher_starts_against_local_queues()
        {
            using var factory = new EventHookFactory();
            var print = factory.GetPrintWatcher();
            AssertStartOkOrExpected(print.Start());
            Thread.Sleep(300);
            print.Stop();
        }

        [Fact]
        [Trait("Category", "Integration")]
        public void ApplicationWatcher_start_stop()
        {
            using var factory = new EventHookFactory();
            var apps = factory.GetApplicationWatcher();
            AssertStartOkOrExpected(apps.Start());
            Thread.Sleep(300);
            apps.Stop();
        }

        [Fact]
        [Trait("Category", "Integration")]
        public void WindowHookEx_start_stop()
        {
            using var hook = new EventHook.Hooks.WindowHookEx();
            AssertStartOkOrExpected(hook.Start());
            Thread.Sleep(200);
            hook.Stop();
        }
    }
}
