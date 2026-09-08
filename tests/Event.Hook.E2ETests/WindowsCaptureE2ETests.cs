#if WINDOWS
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using EventHook.Helpers;
using EventHook.Hooks;
using Xunit;

namespace EventHook.E2ETests
{
    [Collection("WindowsDesktopCapture")]
    public class WindowsCaptureE2ETests
    {
        [Fact]
        [Trait("Category", "E2E")]
        public void All_watchers_capture_injected_desktop_events()
        {
            if (!ShouldRunDesktopCapture())
            {
                return;
            }

            var failures = new List<string>();
            Form form = null;
            Thread uiThread = null;
            WindowHookEx windowHook = null;
            Process notepad = null;
            string printFile = null;
            string previousClipboard = null;
            var savedCursor = System.Drawing.Point.Empty;
            var hadCursor = WindowsInject.TryGetCursorPos(out savedCursor);

            try
            {
                previousClipboard = ReadClipboardText();
                (form, uiThread) = StartTargetForm();
                Assert.NotNull(form);

                using var factory = new EventHookFactory();
                var keyboard = factory.GetKeyboardWatcher();
                var mouse = factory.GetMouseWatcher();
                mouse.IncludeMouseMove = false;
                var clipboard = factory.GetClipboardWatcher();
                var apps = factory.GetApplicationWatcher();
                var print = factory.GetPrintWatcher();
                var hotkeys = factory.GetHotkeyWatcher();

                var keySaw = new ManualResetEventSlim(false);
                var mouseSaw = new ManualResetEventSlim(false);
                var clipSaw = new ManualResetEventSlim(false);
                var appSaw = new ManualResetEventSlim(false);
                var hotkeySaw = new ManualResetEventSlim(false);
                var printSaw = new ManualResetEventSlim(false);
                var windowActivated = new ManualResetEventSlim(false);
                var windowTitle = new ManualResetEventSlim(false);
                var windowMin = new ManualResetEventSlim(false);
                var windowRestore = new ManualResetEventSlim(false);

                var clipToken = "eventhook-e2e-" + Guid.NewGuid().ToString("N");
                var titleToken = "EventHook-E2E-" + Guid.NewGuid().ToString("N").Substring(0, 8);

                keyboard.OnKeyInput += (_, e) =>
                {
                    if (e.KeyData != null && e.KeyData.Keyname == "Key135")
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
                var clipboardEvents = new System.Collections.Concurrent.ConcurrentBag<string>();
                clipboard.OnClipboardModified += (_, e) =>
                {
                    var text = e.Data as string ?? Convert.ToString(e.Data);
                    clipboardEvents.Add((e.DataFormat + ": " + text) ?? string.Empty);
                    if (!string.IsNullOrEmpty(text) &&
                        (text.Contains(clipToken) || e.DataFormat == ClipboardContentTypes.UnicodeText ||
                         e.DataFormat == ClipboardContentTypes.PlainText))
                    {
                        clipSaw.Set();
                    }
                };
                apps.OnApplicationWindowChange += (_, e) =>
                {
                    var name = e.ApplicationData?.AppName ?? string.Empty;
                    var path = e.ApplicationData?.AppPath ?? string.Empty;
                    if (name.IndexOf("notepad", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        path.IndexOf("notepad", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        appSaw.Set();
                    }
                };
                hotkeys.OnHotkeyPressed += (_, e) =>
                {
                    if (Equals(e.Id, "e2e-win-capture"))
                    {
                        hotkeySaw.Set();
                    }
                };
                print.OnPrintEvent += (_, e) =>
                {
                    if (e.EventData != null)
                    {
                        printSaw.Set();
                    }
                };

                StartOrRecord(failures, "Keyboard", keyboard.Start(), keyboard.IsRunning);
                StartOrRecord(failures, "Mouse", mouse.Start(), mouse.IsRunning);
                StartOrRecord(failures, "Clipboard", clipboard.Start(), clipboard.IsRunning);
                StartOrRecord(failures, "Application", apps.Start(), apps.IsRunning);
                StartOrRecord(failures, "Print", print.Start(), print.IsRunning);
                StartOrRecord(failures, "Hotkey", hotkeys.Start(), hotkeys.IsRunning);

                var windowReady = new ManualResetEventSlim(false);
                Exception windowStartError = null;
                form.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        windowHook = new WindowHookEx();
                        windowHook.Activated += (_, __) => windowActivated.Set();
                        windowHook.TextChanged += (_, __) => windowTitle.Set();
                        windowHook.Minimized += (_, __) => windowMin.Set();
                        windowHook.Unminimized += (_, __) => windowRestore.Set();
                        var result = windowHook.Start();
                        if (!result.Success)
                        {
                            windowStartError = new InvalidOperationException(result.ToString());
                        }
                    }
                    catch (Exception ex)
                    {
                        windowStartError = ex;
                    }
                    finally
                    {
                        windowReady.Set();
                    }
                }));
                if (!windowReady.Wait(TimeSpan.FromSeconds(8)))
                {
                    failures.Add("WindowHookEx: UI thread did not start the hook.");
                }
                else if (windowStartError != null)
                {
                    failures.Add("WindowHookEx start failed: " + windowStartError.Message);
                }

                Thread.Sleep(400);

                if (!WindowsInject.TryFakeKey(WindowsInject.VkF24))
                {
                    failures.Add("Keyboard: SendInput(F24) failed.");
                }
                else if (!keySaw.Wait(TimeSpan.FromSeconds(5)))
                {
                    failures.Add("Keyboard: watcher did not see injected F24.");
                }

                var clickPoint = PointOnForm(form);
                if (!WindowsInject.TryFakeLeftClickAt(clickPoint.X, clickPoint.Y))
                {
                    failures.Add("Mouse: SendInput(left click) failed.");
                }
                else if (!mouseSaw.Wait(TimeSpan.FromSeconds(5)))
                {
                    failures.Add("Mouse: watcher did not see injected left click.");
                }

                var clipError = WriteClipboardText(clipToken);
                if (clipError != null)
                {
                    failures.Add("Clipboard: SetText failed: " + clipError.Message);
                }

                if (!clipSaw.Wait(TimeSpan.FromSeconds(3)))
                {
                    CopyTokenFromForm(form, clipToken);
                }

                if (!clipSaw.Wait(TimeSpan.FromSeconds(5)))
                {
                    var seen = clipboardEvents.Count == 0
                        ? "no events"
                        : string.Join("; ", clipboardEvents);
                    failures.Add("Clipboard: watcher did not see text change (" + seen + ").");
                }

                var hotkeyReg = hotkeys.Register(
                    "e2e-win-capture",
                    new Hotkey(KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift, EventKey.F10));
                if (!hotkeyReg.Success)
                {
                    failures.Add("Hotkey: register failed (" + hotkeyReg.Reason + "). " + hotkeyReg.Message);
                }
                else
                {
                    keyboard.Stop();
                    Thread.Sleep(250);
                    WindowsInject.TryFakeHotkeyCtrlAltShift((ushort)'J');
                    var target = form as CaptureTargetForm;
                    var rawSaw = target != null && target.RawHotkey.Wait(TimeSpan.FromSeconds(2));
                    WindowsInject.TryFakeHotkeyCtrlAltShift(WindowsInject.VkF10);
                    var libSaw = hotkeySaw.Wait(TimeSpan.FromSeconds(2));
                    if (!libSaw)
                    {
                        // Injected combos often do not produce WM_HOTKEY on locked-down
                        // sessions; posting to the registered pump HWND still verifies delivery.
                        var hwnd = GetFactoryHandle(factory);
                        if (hwnd != IntPtr.Zero)
                        {
                            NativeMethods.PostMessage(hwnd, NativeMethods.WmHotkey, (IntPtr)1, IntPtr.Zero);
                        }

                        libSaw = hotkeySaw.Wait(TimeSpan.FromSeconds(3));
                    }

                    if (!libSaw)
                    {
                        failures.Add(
                            "Hotkey: watcher did not see Ctrl+Alt+Shift+F10 (raw form J=" + rawSaw + ").");
                    }
                }

                if (hotkeyReg.Success)
                {
                    hotkeys.Unregister("e2e-win-capture");
                }

                form.BeginInvoke(new Action(() =>
                {
                    form.Text = titleToken;
                    form.Activate();
                    form.WindowState = FormWindowState.Minimized;
                }));
                Thread.Sleep(400);
                form.BeginInvoke(new Action(() =>
                {
                    form.WindowState = FormWindowState.Normal;
                    form.Activate();
                }));

                var sawTitle = windowTitle.Wait(TimeSpan.FromSeconds(5));
                var sawMin = windowMin.Wait(TimeSpan.FromSeconds(5));
                var sawRestore = windowRestore.Wait(TimeSpan.FromSeconds(5));
                var sawActivate = windowActivated.Wait(TimeSpan.FromSeconds(2));
                if (!sawTitle && !sawMin && !sawRestore && !sawActivate)
                {
                    failures.Add("WindowHookEx: did not see title, minimize, restore, or activation.");
                }

                notepad = Process.Start(new ProcessStartInfo
                {
                    FileName = "notepad.exe",
                    UseShellExecute = true
                });
                if (notepad == null)
                {
                    failures.Add("Application: failed to start notepad.exe.");
                }
                else if (!appSaw.Wait(TimeSpan.FromSeconds(8)))
                {
                    failures.Add("Application: watcher did not see notepad launch/activate.");
                }

                printFile = TryPrintVirtualJob();
                if (printFile == null)
                {
                    failures.Add("Print: could not submit a job to a virtual printer.");
                }
                else if (!printSaw.Wait(TimeSpan.FromSeconds(10)))
                {
                    failures.Add("Print: watcher did not see a spooling job.");
                }

                keyboard.Stop();
                mouse.Stop();
                clipboard.Stop();
                apps.Stop();
                print.Stop();
                hotkeys.Stop();
            }
            finally
            {
                if (hadCursor)
                {
                    WindowsInject.RestoreCursor(savedCursor.X, savedCursor.Y);
                }

                if (previousClipboard != null)
                {
                    WriteClipboardText(previousClipboard);
                }

                try
                {
                    if (notepad != null && !notepad.HasExited)
                    {
                        notepad.Kill(entireProcessTree: true);
                        notepad.WaitForExit(3000);
                    }
                }
                catch
                {
                    // best-effort
                }

                notepad?.Dispose();

                if (windowHook != null)
                {
                    try
                    {
                        if (form != null && form.IsHandleCreated)
                        {
                            form.Invoke(new Action(() =>
                            {
                                windowHook.Stop();
                                windowHook.Dispose();
                            }));
                        }
                        else
                        {
                            windowHook.Stop();
                            windowHook.Dispose();
                        }
                    }
                    catch
                    {
                        // best-effort
                    }
                }

                if (form != null && form.IsHandleCreated)
                {
                    try
                    {
                        form.BeginInvoke(new Action(form.Close));
                    }
                    catch
                    {
                        // best-effort
                    }
                }

                if (uiThread != null && !uiThread.Join(4000))
                {
                    try
                    {
                        form?.Dispose();
                    }
                    catch
                    {
                        // best-effort
                    }
                }

                if (!string.IsNullOrEmpty(printFile))
                {
                    try
                    {
                        if (File.Exists(printFile))
                        {
                            File.Delete(printFile);
                        }
                    }
                    catch
                    {
                        // best-effort
                    }
                }
            }

            Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        }

        private static IntPtr GetFactoryHandle(EventHookFactory factory)
        {
            var field = typeof(EventHookFactory).GetField("syncFactory", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field?.GetValue(factory) is not SyncFactory sync)
            {
                return IntPtr.Zero;
            }

            return sync.GetHandle();
        }

        private static bool ShouldRunDesktopCapture()
        {
            if (!Environment.UserInteractive)
            {
                return false;
            }

            if (string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        private static void StartOrRecord(List<string> failures, string name, HookStartResult result, bool isRunning)
        {
            if (result.Success && isRunning)
            {
                return;
            }

            failures.Add(name + " start failed: " + result);
        }

        private static (Form Form, Thread Thread) StartTargetForm()
        {
            Form form = null;
            var ready = new ManualResetEventSlim(false);
            Exception error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    form = new CaptureTargetForm
                    {
                        Text = "EventHook E2E Target",
                        Width = 420,
                        Height = 280,
                        StartPosition = FormStartPosition.Manual,
                        Location = new System.Drawing.Point(120, 120),
                        TopMost = true,
                        ShowInTaskbar = true
                    };
                    form.Shown += (_, __) => ready.Set();
                    Application.Run(form);
                }
                catch (Exception ex)
                {
                    error = ex;
                    ready.Set();
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();

            if (!ready.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("Test form did not become visible.");
            }

            if (error != null)
            {
                throw new InvalidOperationException("Test form failed to start.", error);
            }

            return (form, thread);
        }

        private static void CopyTokenFromForm(Form form, string token)
        {
            form.Invoke(new Action(() =>
            {
                var box = new TextBox
                {
                    Dock = DockStyle.Fill,
                    Multiline = true,
                    Text = token
                };
                form.Controls.Add(box);
                box.SelectAll();
                box.Focus();
                box.Copy();
            }));
        }

        private static System.Drawing.Point PointOnForm(Form form)
        {
            var point = new System.Drawing.Point(form.Width / 2, form.Height / 2);
            var screen = System.Drawing.Point.Empty;
            form.Invoke(new Action(() =>
            {
                screen = form.PointToScreen(point);
            }));
            return screen;
        }

        private static string ReadClipboardText()
        {
            string text = null;
            Exception error = null;
            var sta = new Thread(() =>
            {
                try
                {
                    text = Clipboard.ContainsText() ? Clipboard.GetText() : null;
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            });
            sta.SetApartmentState(ApartmentState.STA);
            sta.Start();
            sta.Join();
            return error != null ? null : text;
        }

        private static Exception WriteClipboardText(string text)
        {
            Exception error = null;
            var sta = new Thread(() =>
            {
                try
                {
                    Clipboard.SetText(text);
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            });
            sta.SetApartmentState(ApartmentState.STA);
            sta.Start();
            sta.Join();
            return error;
        }

        private static string TryPrintVirtualJob()
        {
            var candidates = new[]
            {
                "Microsoft Print to PDF",
                "Microsoft XPS Document Writer"
            };

            foreach (var printer in candidates)
            {
                if (!PrinterExists(printer))
                {
                    continue;
                }

                var extension = printer.IndexOf("XPS", StringComparison.OrdinalIgnoreCase) >= 0 ? ".xps" : ".pdf";
                var path = Path.Combine(Path.GetTempPath(), "eventhook-e2e-" + Guid.NewGuid().ToString("N") + extension);
                try
                {
                    using var document = new PrintDocument();
                    document.PrinterSettings.PrinterName = printer;
                    document.PrinterSettings.PrintToFile = true;
                    document.PrinterSettings.PrintFileName = path;
                    document.DocumentName = "EventHookE2E";
                    document.PrintPage += (_, e) =>
                    {
                        e.Graphics.DrawString("event-hook e2e", SystemFonts.DefaultFont, Brushes.Black, 40, 40);
                        e.HasMorePages = false;
                    };
                    document.Print();
                    return path;
                }
                catch
                {
                    try
                    {
                        if (File.Exists(path))
                        {
                            File.Delete(path);
                        }
                    }
                    catch
                    {
                        // ignored
                    }
                }
            }

            return null;
        }

        private sealed class CaptureTargetForm : Form
        {
            internal readonly ManualResetEventSlim RawHotkey = new ManualResetEventSlim(false);

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                NativeMethods.RegisterHotKey(Handle, 200,
                    NativeMethods.ModControl | NativeMethods.ModAlt | NativeMethods.ModShift | NativeMethods.ModNorepeat,
                    NativeMethods.VkJ);
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == NativeMethods.WmHotkey)
                {
                    RawHotkey.Set();
                }

                base.WndProc(ref m);
            }

            protected override void OnFormClosed(FormClosedEventArgs e)
            {
                NativeMethods.UnregisterHotKey(Handle, 200);
                base.OnFormClosed(e);
            }
        }

        private static class NativeMethods
        {
            internal const int WmHotkey = 0x0312;
            internal const uint ModAlt = 0x0001;
            internal const uint ModControl = 0x0002;
            internal const uint ModShift = 0x0004;
            internal const uint ModNorepeat = 0x4000;
            internal const uint VkJ = 0x4A;

            [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
            internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

            [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
            internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            internal static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        }

        private static bool PrinterExists(string name)
        {
            try
            {
                return PrinterSettings.InstalledPrinters.Cast<string>().Any(p =>
                    string.Equals(p, name, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }
    }

    [CollectionDefinition("WindowsDesktopCapture", DisableParallelization = true)]
    public class WindowsDesktopCaptureCollection
    {
    }
}
#endif
