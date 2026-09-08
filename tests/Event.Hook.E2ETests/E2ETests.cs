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

        [Fact]
        [Trait("Category", "E2E")]
        public void Mac_quartz_keyboard_mouse_and_hotkey_are_observed()
        {
            if (!OperatingSystem.IsMacOS())
            {
                return;
            }

            using var factory = new EventHookFactory();
            var kb = factory.GetKeyboardWatcher();
            var mouse = factory.GetMouseWatcher();
            var hotkeys = factory.GetHotkeyWatcher();
            mouse.IncludeMouseMove = false;

            var keySaw = new ManualResetEventSlim(false);
            var mouseSaw = new ManualResetEventSlim(false);
            var hotkeySaw = new ManualResetEventSlim(false);
            kb.OnKeyInput += (_, e) =>
            {
                if (e.KeyData != null && e.KeyData.Keyname == "A")
                {
                    keySaw.Set();
                }
            };
            mouse.OnMouseInput += (_, e) =>
            {
                if (e.Message == MouseMessages.WM_LBUTTONDOWN ||
                    e.Message == MouseMessages.WM_LBUTTONUP ||
                    e.Message == MouseMessages.WM_MOUSEWHEEL)
                {
                    mouseSaw.Set();
                }
            };
            hotkeys.OnHotkeyPressed += (_, e) =>
            {
                if (Equals(e.Id, "e2e-mac"))
                {
                    hotkeySaw.Set();
                }
            };

            kb.Start().ThrowIfFailed();
            mouse.Start().ThrowIfFailed();
            hotkeys.Start().ThrowIfFailed();
            var reg = hotkeys.Register(
                "e2e-mac",
                new Hotkey(KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift, EventKey.F11));
            if (!reg.Success)
            {
                kb.Stop();
                mouse.Stop();
                hotkeys.Stop();
                Assert.Equal(HookFailureReason.AlreadyInUse, reg.Reason);
                return;
            }

            Assert.True(MacCgEventInject.TryFocusTextEdit(), "Could not activate TextEdit as a safe injection target.");
            Task.Delay(400).GetAwaiter().GetResult();
            Assert.True(MacCgEventInject.TryFakeKeyA(), "CGEvent key injection failed.");
            Assert.True(MacCgEventInject.TryFakeScroll() || MacCgEventInject.TryFakeLeftClick(), "CGEvent mouse injection failed.");
            Assert.True(MacCgEventInject.TryFakeHotkeyCtrlAltShiftF11(), "CGEvent hotkey injection failed.");

            Assert.True(keySaw.Wait(TimeSpan.FromSeconds(6)), "Keyboard watcher did not see injected 'A'.");
            Assert.True(mouseSaw.Wait(TimeSpan.FromSeconds(6)), "Mouse watcher did not see injected scroll/click.");
            Assert.True(hotkeySaw.Wait(TimeSpan.FromSeconds(6)), "Hotkey watcher did not see Ctrl+Alt+Shift+F11.");

            hotkeys.Unregister("e2e-mac");
            kb.Stop();
            mouse.Stop();
            hotkeys.Stop();
        }

        [Fact]
        [Trait("Category", "E2E")]
        public void Mac_clipboard_and_application_events_are_observed()
        {
            if (!OperatingSystem.IsMacOS())
            {
                return;
            }

            using var factory = new EventHookFactory();
            var clip = factory.GetClipboardWatcher();
            var apps = factory.GetApplicationWatcher();
            var marker = "eventhook-e2e-mac-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var clipSaw = new ManualResetEventSlim(false);
            var appSaw = new ManualResetEventSlim(false);

            clip.OnClipboardModified += (_, e) =>
            {
                if (e.Data != null && e.Data.ToString().Contains(marker))
                {
                    clipSaw.Set();
                }
            };
            apps.OnApplicationWindowChange += (_, e) =>
            {
                if (string.Equals(e.ApplicationData.AppName, "Stickies", StringComparison.OrdinalIgnoreCase) &&
                    (e.Event == ApplicationEvents.Launched || e.Event == ApplicationEvents.Activated))
                {
                    appSaw.Set();
                }
            };

            clip.Start().ThrowIfFailed();
            apps.Start().ThrowIfFailed();
            Task.Delay(700).GetAwaiter().GetResult();

            Assert.True(MacCgEventInject.TryCopyText(marker), "pbcopy failed.");
            Assert.True(MacCgEventInject.TryLaunchStickies(), "open -a Stickies failed.");

            Assert.True(clipSaw.Wait(TimeSpan.FromSeconds(6)), "Clipboard watcher did not see pbcopy text.");
            Assert.True(appSaw.Wait(TimeSpan.FromSeconds(8)), "Application watcher did not see Stickies.");

            MacCgEventInject.TryQuitStickies();
            clip.Stop();
            apps.Stop();
        }

        [Fact]
        [Trait("Category", "E2E")]
        public void Mac_print_job_is_observed()
        {
            if (!OperatingSystem.IsMacOS())
            {
                return;
            }

            using var printer = new MacCupsPrinter();
            if (!printer.TryStart())
            {
                return;
            }

            using var factory = new EventHookFactory();
            var print = factory.GetPrintWatcher();
            var saw = new ManualResetEventSlim(false);
            print.OnPrintEvent += (_, e) =>
            {
                if (e.EventData != null &&
                    (string.Equals(e.EventData.PrinterName, printer.Name, StringComparison.OrdinalIgnoreCase) ||
                     (e.EventData.JobName != null && e.EventData.JobName.Contains("eventhook-e2e"))))
                {
                    saw.Set();
                }
            };

            print.Start().ThrowIfFailed();
            Task.Delay(900).GetAwaiter().GetResult();
            Assert.True(printer.TryPrint("eventhook-e2e-job", "event-hook macOS print proof"), "lp failed.");
            Assert.True(saw.Wait(TimeSpan.FromSeconds(12)), "Print watcher did not see the CUPS job.");
            print.Stop();
        }

        [Fact]
        [Trait("Category", "E2E")]
        public void Mac_windowhookex_sees_textedit_title_or_focus()
        {
            if (!OperatingSystem.IsMacOS())
            {
                return;
            }

            using var hook = new WindowHookEx();
            var saw = new ManualResetEventSlim(false);
            hook.Activated += (_, _) => saw.Set();
            hook.TextChanged += (_, _) => saw.Set();
            hook.Minimized += (_, _) => saw.Set();
            hook.Unminimized += (_, _) => saw.Set();

            var start = hook.Start();
            if (!start.Success)
            {
                Assert.Equal(HookFailureReason.PermissionDenied, start.Reason);
                return;
            }

            Assert.True(MacCgEventInject.TryFocusTextEdit(), "Could not activate TextEdit.");
            Task.Delay(800).GetAwaiter().GetResult();
            var finder = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "osascript",
                UseShellExecute = false
            };
            finder.ArgumentList.Add("-e");
            finder.ArgumentList.Add("tell application \"Finder\" to activate");
            System.Diagnostics.Process.Start(finder)?.WaitForExit(4000);
            Task.Delay(500).GetAwaiter().GetResult();
            MacCgEventInject.TryFocusTextEdit();
            MacCgEventInject.TrySetTextEditDocumentName("eventhook-e2e-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            MacCgEventInject.TryMinimizeTextEdit();
            Assert.True(saw.Wait(TimeSpan.FromSeconds(8)), "WindowHookEx did not see TextEdit focus, title, or minimize.");
            hook.Stop();
        }
#endif
    }
}
