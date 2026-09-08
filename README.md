# EventHook

A .NET library to subscribe to global user actions across **Windows**, **macOS**, and **Linux**: keyboard, mouse, clipboard, application windows, print jobs, and hotkeys.

[![CI](https://github.com/justcoding121/event-hook/actions/workflows/ci.yml/badge.svg?branch=develop)](https://github.com/justcoding121/event-hook/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/EventHook.svg)](https://www.nuget.org/packages/EventHook)
[![NuGet downloads](https://img.shields.io/nuget/dt/EventHook.svg)](https://www.nuget.org/packages/EventHook)

* [API Documentation](https://justcoding121.github.io/event-hook/) (DocFX site on `develop`)

## Install

```bash
dotnet add package EventHook
```

| Platform | Target framework |
|---|---|
| Windows | `net10.0-windows` |
| macOS / Linux | `net10.0` |

Windows applications must target `net10.0-windows`. The portable `net10.0` package is for macOS and Linux.

## Sample

```csharp
using System;

using (var eventHookFactory = new EventHookFactory())
{
    var keyboardWatcher = eventHookFactory.GetKeyboardWatcher();
    var kb = keyboardWatcher.Start();
    if (!kb.Success)
    {
        Console.WriteLine(kb); // PermissionDenied / PrivilegeRequired / etc.
    }
    else
    {
        keyboardWatcher.OnKeyInput += (s, e) =>
            Console.WriteLine($"Key {e.KeyData.EventType} of {e.KeyData.Keyname}");
    }

    var mouseWatcher = eventHookFactory.GetMouseWatcher();
    mouseWatcher.IncludeMouseMove = false; // default is false
    mouseWatcher.Start().ThrowIfFailed();
    mouseWatcher.OnMouseInput += (s, e) =>
        Console.WriteLine($"Mouse {e.Message} at {e.Point.x},{e.Point.y}");

    var clipboardWatcher = eventHookFactory.GetClipboardWatcher();
    clipboardWatcher.Start().ThrowIfFailed();
    clipboardWatcher.OnClipboardModified += (s, e) =>
        Console.WriteLine($"Clipboard {e.DataFormat}: {e.Data}");

    var applicationWatcher = eventHookFactory.GetApplicationWatcher();
    applicationWatcher.Start().ThrowIfFailed();
    applicationWatcher.OnApplicationWindowChange += (s, e) =>
        Console.WriteLine($"{e.ApplicationData.AppName} was {e.Event}");

    var printWatcher = eventHookFactory.GetPrintWatcher();
    printWatcher.Start().ThrowIfFailed();
    printWatcher.OnPrintEvent += (s, e) =>
        Console.WriteLine($"Printer {e.EventData.PrinterName} pages={e.EventData.Pages}");

    var hotkeyWatcher = eventHookFactory.GetHotkeyWatcher();
    hotkeyWatcher.Start().ThrowIfFailed();
    hotkeyWatcher.Register("demo", new Hotkey(KeyModifiers.Control | KeyModifiers.Alt, EventKey.H))
        .ThrowIfFailed();
    hotkeyWatcher.OnHotkeyPressed += (s, e) =>
        Console.WriteLine($"Hotkey {e.Id} ({e.Hotkey})");

    Console.ReadLine();
}
```

OS hook callbacks only copy a lightweight snapshot and return immediately. Decoding and user event handlers run on a dedicated offload path so input is never blocked.

### Permissions and failures

`Start()` / `Register()` return `HookStartResult`. Check `Success`, or call `ThrowIfFailed()`. `IsRunning` is true only after a successful install.

| Reason | Typical cause |
|---|---|
| `PermissionDenied` | macOS Input Monitoring / Accessibility (TCC) |
| `PrivilegeRequired` | Linux `/dev/input` not readable (add user to `input` group) |
| `DisplayUnavailable` | No `DISPLAY` / session when required (Linux/macOS). Not used for a missing Win32 pump. |
| `NotSupportedOnPlatform` | Feature needs X11 on Linux Wayland-only, or wrong TFM on Windows |
| `AlreadyInUse` | Hotkey already registered by another process |
| `NativeFailure` | Native API failed after permissions were OK |

If `EventHookFactory` cannot start its own Windows message pump (or macOS cannot start its `CFRunLoop`), construction throws `TimeoutException`. That is not a `HookStartResult` — there is no `HookFailureReason` for “pump missing.”

See [examples/MAC.md](examples/MAC.md) and [examples/LINUX.md](examples/LINUX.md) for host setup.

### Message pump / event loop

Hooks need an OS event loop. Callers usually do **not** create one: the factory (Windows) or platform hosts (macOS / Linux) start it.

| Platform | Who pumps | What you must do |
|---|---|---|
| Windows | `EventHookFactory` | Nothing in console/service code. Construct the factory on an STA UI thread **or** let it create a background STA WinForms loop (`EventHook.MessagePump`). |
| macOS | Library `CFRunLoop` (`EventHook.Mac.CFRunLoop`) | Nothing. The HWND argument is ignored. |
| Linux X11 | Library `XNextEvent` thread | Need `DISPLAY` (or Xvfb). HWND is ignored. |
| Linux evdev | `/dev/input` reads (no X loop) | User must be in the `input` group. Clipboard / windows / hotkeys still need X11. |

**Windows details**

- Keyboard and mouse (`WH_KEYBOARD_LL` / `WH_MOUSE_LL`) are installed on the factory pump thread. That thread must keep pumping or Windows stops delivering.
- Clipboard (`WM_CLIPBOARDUPDATE`), hotkeys (`WM_HOTKEY`), and application/shell hooks are HWND messages on the same pump.
- `WindowHookEx` does **not** use the factory pump. Call `Start()` from a thread that already pumps messages (your UI thread).
- Hosted COM / Office add-ins: construct the factory on the STA UI thread, or pass that window’s HWND. A provided HWND must keep pumping. EventHook registers hotkeys on that handle but does **not** subclass it — if you pass a host HWND, **your** `WndProc` must dispatch `WM_HOTKEY` or the watcher will report `IsRunning` and still never fire.
- Prefer the default `new EventHookFactory()` (library-owned pump) unless you are hosting and will forward messages.

### Application window filter (Windows)

```csharp
EventHook.Helpers.AppWindowFilter.IncludeWindowsWithoutSysMenu = true;
EventHook.Helpers.AppWindowFilter.IncludeDialogs = true;
EventHook.Helpers.AppWindowFilter.CustomFilter = hwnd => true;
```

### Hosted apps (COM / Office add-ins on Windows)

Prefer constructing the factory on an STA UI thread, or pass an existing message-pump HWND:

```csharp
using var factory = new EventHookFactory(hostMainWindowHandle);
```

## Development

- .NET 10 SDK
- Windows: `dotnet build src/Event.Hook.sln -c Release`
- Portable (macOS/Linux CI): build the library, tests, and console example projects (not the full Windows-only solution)
- `dotnet test tests/Event.Hook.Tests`
- `dotnet test tests/Event.Hook.IntegrationTests`
- `dotnet test tests/Event.Hook.E2ETests --filter Category=E2E`
- Docs: `dotnet tool restore` then `docfx .github/docfx.json`

### Release branches

| Branch | NuGet | Notes |
|---|---|---|
| `develop` | (no publish) | CI on Windows + macOS + Linux; SonarCloud + DocFX on develop push only |
| `beta` | `{VersionPrefix}-beta.2` | Same CI, then publish prerelease (`3.0.0-beta` already shipped) |
| `stable` / tag `v*` | `{VersionPrefix}` | Same CI, then stable release + GitHub Pages docs |

Publishing uses NuGet Trusted Publishing (`NUGET_USER` on the `nuget-publish` environment).

## Version 3.0 notes

Breaking: multi-platform (`net10.0-windows` + `net10.0`), `Start()`/`Register()` return `HookStartResult`, portable `Hotkey` replaces WinForms `Keys`, `IncludeMouseMove` defaults to `false`, non-blocking OS hook offload.

Repository renamed from `windows-user-action-hook` to [event-hook](https://github.com/justcoding121/event-hook). Solution/projects use `Event.Hook.*`; NuGet package id remains `EventHook`.

## Code quality

[![Quality Gate](https://sonarcloud.io/api/project_badges/measure?project=justcoding121_windows-user-action-hook&metric=alert_status)](https://sonarcloud.io/summary/overall?id=justcoding121_windows-user-action-hook&branch=develop)
[![Coverage](https://sonarcloud.io/api/project_badges/measure?project=justcoding121_windows-user-action-hook&metric=coverage)](https://sonarcloud.io/summary/overall?id=justcoding121_windows-user-action-hook&branch=develop)
[![Lines of Code](https://sonarcloud.io/api/project_badges/measure?project=justcoding121_windows-user-action-hook&metric=ncloc)](https://sonarcloud.io/summary/overall?id=justcoding121_windows-user-action-hook&branch=develop)
[![Bugs](https://sonarcloud.io/api/project_badges/measure?project=justcoding121_windows-user-action-hook&metric=bugs)](https://sonarcloud.io/summary/overall?id=justcoding121_windows-user-action-hook&branch=develop)
[![Vulnerabilities](https://sonarcloud.io/api/project_badges/measure?project=justcoding121_windows-user-action-hook&metric=vulnerabilities)](https://sonarcloud.io/summary/overall?id=justcoding121_windows-user-action-hook&branch=develop)
[![Code Smells](https://sonarcloud.io/api/project_badges/measure?project=justcoding121_windows-user-action-hook&metric=code_smells)](https://sonarcloud.io/summary/overall?id=justcoding121_windows-user-action-hook&branch=develop)
[![Security Rating](https://sonarcloud.io/api/project_badges/measure?project=justcoding121_windows-user-action-hook&metric=security_rating)](https://sonarcloud.io/summary/overall?id=justcoding121_windows-user-action-hook&branch=develop)
[![Reliability Rating](https://sonarcloud.io/api/project_badges/measure?project=justcoding121_windows-user-action-hook&metric=reliability_rating)](https://sonarcloud.io/summary/overall?id=justcoding121_windows-user-action-hook&branch=develop)
[![Maintainability Rating](https://sonarcloud.io/api/project_badges/measure?project=justcoding121_windows-user-action-hook&metric=sqale_rating)](https://sonarcloud.io/summary/overall?id=justcoding121_windows-user-action-hook&branch=develop)
[![Duplicated Lines](https://sonarcloud.io/api/project_badges/measure?project=justcoding121_windows-user-action-hook&metric=duplicated_lines_density)](https://sonarcloud.io/summary/overall?id=justcoding121_windows-user-action-hook&branch=develop)
[![Technical Debt](https://sonarcloud.io/api/project_badges/measure?project=justcoding121_windows-user-action-hook&metric=sqale_index)](https://sonarcloud.io/summary/overall?id=justcoding121_windows-user-action-hook&branch=develop)
