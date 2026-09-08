using System;
using System.Threading;
using Xunit;

namespace EventHook.E2ETests
{
    public class HookE2ETests
    {
        [Fact]
        [Trait("Category", "E2E")]
        public void Keyboard_start_is_running_or_expected_platform_result()
        {
            using var factory = new EventHookFactory();
            var kb = factory.GetKeyboardWatcher();
            var result = kb.Start();

            if (result.Success)
            {
                Assert.True(kb.IsRunning);
                Thread.Sleep(150);
                kb.Stop();
                Assert.False(kb.IsRunning);
                return;
            }

            Assert.False(kb.IsRunning);
            Assert.Contains(result.Reason, new[]
            {
                HookFailureReason.PermissionDenied,
                HookFailureReason.PrivilegeRequired,
                HookFailureReason.DisplayUnavailable,
                HookFailureReason.NotSupportedOnPlatform,
                HookFailureReason.NativeFailure
            });
            Assert.False(string.IsNullOrWhiteSpace(result.Message));
        }

        [Fact]
        [Trait("Category", "E2E")]
        public void Mouse_start_is_running_or_expected_platform_result()
        {
            using var factory = new EventHookFactory();
            var mouse = factory.GetMouseWatcher();
            mouse.IncludeMouseMove = false;
            var result = mouse.Start();

            if (result.Success)
            {
                Assert.True(mouse.IsRunning);
                Thread.Sleep(150);
                mouse.Stop();
                return;
            }

            Assert.False(mouse.IsRunning);
            Assert.NotEqual(HookFailureReason.None, result.Reason);
            Assert.False(string.IsNullOrWhiteSpace(result.Message));
        }

        [Fact]
        [Trait("Category", "E2E")]
        public void Hotkey_register_succeeds_or_reports_conflict_or_unsupported()
        {
            using var factory = new EventHookFactory();
            var hotkeys = factory.GetHotkeyWatcher();
            var start = hotkeys.Start();
            if (!start.Success)
            {
                Assert.NotEqual(HookFailureReason.None, start.Reason);
                return;
            }

            var reg = hotkeys.Register("e2e",
                new Hotkey(KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift, EventKey.F11));
            if (reg.Success)
            {
                hotkeys.Unregister("e2e");
            }
            else
            {
                Assert.Contains(reg.Reason, new[]
                {
                    HookFailureReason.AlreadyInUse,
                    HookFailureReason.NotSupportedOnPlatform,
                    HookFailureReason.DisplayUnavailable,
                    HookFailureReason.NativeFailure
                });
            }

            hotkeys.Stop();
        }
    }
}
