using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Xunit;

namespace EventHook.IntegrationTests
{
    public class LifecycleIntegrationTests
    {
        [Fact]
        [Trait("Category", "Integration")]
        public void Factory_start_stop_keyboard_and_mouse_exits_cleanly()
        {
            using var factory = new EventHookFactory();
            var kb = factory.GetKeyboardWatcher();
            var mouse = factory.GetMouseWatcher();
            mouse.IncludeMouseMove = false;

            kb.Start();
            mouse.Start();
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
            hotkeys.Start();
            try
            {
                hotkeys.Register("it", Keys.Control | Keys.Alt | Keys.Shift | Keys.F12);
            }
            catch (InvalidOperationException)
            {
                // Hotkey may already be owned by another process in the session.
                hotkeys.Stop();
                return;
            }

            Thread.Sleep(100);
            hotkeys.Unregister("it");
            hotkeys.Stop();
        }

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
            clip.Start();

            Exception staError = null;
            var sta = new Thread(() =>
            {
                try
                {
                    Clipboard.SetText("eventhook-it-" + Guid.NewGuid());
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
                // Clipboard may be locked by another process in CI/desktop sessions.
                clip.Stop();
                return;
            }

            Assert.True(saw.Wait(TimeSpan.FromSeconds(5)), "Expected clipboard modification event.");
            clip.Stop();
        }

        [Fact]
        [Trait("Category", "Integration")]
        public void PrintWatcher_starts_against_local_queues()
        {
            using var factory = new EventHookFactory();
            var print = factory.GetPrintWatcher();
            print.Start();
            Thread.Sleep(300);
            print.Stop();
        }

        [Fact]
        [Trait("Category", "Integration")]
        public void ApplicationWatcher_start_stop()
        {
            using var factory = new EventHookFactory();
            var apps = factory.GetApplicationWatcher();
            apps.Start();
            Thread.Sleep(300);
            apps.Stop();
        }
    }
}
