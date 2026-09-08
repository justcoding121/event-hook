using System;
using System.Threading;
using System.Threading.Tasks;
using EventHook.Hooks;
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
                Task.Delay(150).GetAwaiter().GetResult();
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
                Task.Delay(150).GetAwaiter().GetResult();
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

#if !WINDOWS
        [Fact]
        [Trait("Category", "E2E")]
        public void Linux_xtest_keyboard_and_mouse_click_are_observed()
        {
            if (!OperatingSystem.IsLinux() || !LinuxX11Inject.DisplayAvailable)
            {
                return;
            }

            using var factory = new EventHookFactory();
            var kb = factory.GetKeyboardWatcher();
            var mouse = factory.GetMouseWatcher();
            mouse.IncludeMouseMove = false;

            var keySaw = new ManualResetEventSlim(false);
            var mouseSaw = new ManualResetEventSlim(false);
            kb.OnKeyInput += (_, e) =>
            {
                if (e.KeyData != null && e.KeyData.Keyname == "A")
                {
                    keySaw.Set();
                }
            };
            mouse.OnMouseInput += (_, e) =>
            {
                if (e.Message == MouseMessages.WM_LBUTTONDOWN || e.Message == MouseMessages.WM_LBUTTONUP)
                {
                    mouseSaw.Set();
                }
            };

            kb.Start().ThrowIfFailed();
            mouse.Start().ThrowIfFailed();
            Assert.True(kb.IsRunning);
            Assert.True(mouse.IsRunning);

            Assert.True(LinuxX11Inject.TryFakeKeyA(), "XTest key injection failed.");
            Assert.True(LinuxX11Inject.TryFakeLeftClick(), "XTest click injection failed.");

            Assert.True(keySaw.Wait(TimeSpan.FromSeconds(5)), "Keyboard watcher did not see XTest 'A'.");
            Assert.True(mouseSaw.Wait(TimeSpan.FromSeconds(5)), "Mouse watcher did not see XTest left click.");

            kb.Stop();
            mouse.Stop();
        }

        [Fact]
        [Trait("Category", "E2E")]
        public void Linux_xtest_hotkey_is_observed()
        {
            if (!OperatingSystem.IsLinux() || !LinuxX11Inject.DisplayAvailable)
            {
                return;
            }

            using var factory = new EventHookFactory();
            var hotkeys = factory.GetHotkeyWatcher();
            hotkeys.Start().ThrowIfFailed();

            var saw = new ManualResetEventSlim(false);
            hotkeys.OnHotkeyPressed += (_, e) =>
            {
                if (Equals(e.Id, "e2e-xtest"))
                {
                    saw.Set();
                }
            };

            var reg = hotkeys.Register(
                "e2e-xtest",
                new Hotkey(KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift, EventKey.F11));
            if (!reg.Success)
            {
                hotkeys.Stop();
                Assert.Equal(HookFailureReason.AlreadyInUse, reg.Reason);
                return;
            }

            Assert.True(LinuxX11Inject.TryFakeHotkeyCtrlAltShiftF11(), "XTest hotkey injection failed.");
            Assert.True(saw.Wait(TimeSpan.FromSeconds(5)), "Hotkey watcher did not see Ctrl+Alt+Shift+F11.");

            hotkeys.Unregister("e2e-xtest");
            hotkeys.Stop();
        }
#endif
    }
}
