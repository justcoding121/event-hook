# Windows User Action Hook (EventHook)

[![CI](https://github.com/justcoding121/windows-user-action-hook/actions/workflows/ci.yml/badge.svg?branch=develop)](https://github.com/justcoding121/windows-user-action-hook/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/EventHook.svg)](https://www.nuget.org/packages/EventHook)

## Code Quality

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

A .NET library to subscribe to Windows global user actions: keyboard, mouse, clipboard, application windows, print jobs, and hotkeys.

**Requires:** .NET 10 on Windows (`net10.0-windows`). AnyCPU — works on x86, x64, and ARM64 Windows when P/Invoke pointer sizes are correct (v2 fixes these).

* [API Documentation](https://justcoding121.github.io/windows-user-action-hook/docs/)

## Install

```bash
dotnet add package EventHook
```

## Sample

```csharp
using System;
using System.Windows.Forms;

using (var eventHookFactory = new EventHookFactory())
{
    var keyboardWatcher = eventHookFactory.GetKeyboardWatcher();
    keyboardWatcher.Start();
    keyboardWatcher.OnKeyInput += (s, e) =>
        Console.WriteLine($"Key {e.KeyData.EventType} of {e.KeyData.Keyname}");

    var mouseWatcher = eventHookFactory.GetMouseWatcher();
    mouseWatcher.IncludeMouseMove = false; // #28
    mouseWatcher.Start();
    mouseWatcher.OnMouseInput += (s, e) =>
        Console.WriteLine($"Mouse {e.Message} at {e.Point.x},{e.Point.y}");

    var clipboardWatcher = eventHookFactory.GetClipboardWatcher();
    clipboardWatcher.Start();
    clipboardWatcher.OnClipboardModified += (s, e) =>
        Console.WriteLine($"Clipboard {e.DataFormat}: {e.Data}");

    var applicationWatcher = eventHookFactory.GetApplicationWatcher();
    applicationWatcher.Start();
    applicationWatcher.OnApplicationWindowChange += (s, e) =>
        Console.WriteLine($"{e.ApplicationData.AppName} was {e.Event}");

    var printWatcher = eventHookFactory.GetPrintWatcher();
    printWatcher.Start();
    printWatcher.OnPrintEvent += (s, e) =>
        Console.WriteLine($"Printer {e.EventData.PrinterName} pages={e.EventData.Pages}");

    var hotkeyWatcher = eventHookFactory.GetHotkeyWatcher();
    hotkeyWatcher.Start();
    hotkeyWatcher.Register("demo", Keys.Control | Keys.Alt | Keys.H);
    hotkeyWatcher.OnHotkeyPressed += (s, e) =>
        Console.WriteLine($"Hotkey {e.Id} ({e.Keys})");

    Console.ReadLine();
}
```

### Application window filter

```csharp
EventHook.Helpers.AppWindowFilter.IncludeWindowsWithoutSysMenu = true; // games without WS_SYSMENU
EventHook.Helpers.AppWindowFilter.IncludeDialogs = true;               // MessageBox / #32770
EventHook.Helpers.AppWindowFilter.CustomFilter = hwnd => true;         // optional
```

### Hosted apps (COM / Office add-ins)

Prefer constructing the factory on an STA UI thread, or pass an existing message-pump HWND:

```csharp
using var factory = new EventHookFactory(hostMainWindowHandle);
```

### VB.NET

See `examples/EventHook.VB.Example`. Context-menu paste is observed via the clipboard watcher (global); `WM_PASTE` itself is application-local.

### Print to PDF

`PrintWatcher` enumerates local and connected queues, including virtual printers such as **Microsoft Print to PDF**. Print a document to that queue to verify `OnPrintEvent`.

## Development

- Visual Studio 2022 / .NET 10 SDK
- `dotnet build src/EventHook.sln -c Release`
- `dotnet test tests/EventHook.Tests`
- `dotnet test tests/EventHook.IntegrationTests`
- Docs: `dotnet tool restore` then `dotnet tool run docfx metadata docfx.json` and `dotnet tool run docfx build docfx.json`

### Release branches

| Branch | NuGet | Notes |
|---|---|---|
| `develop` | (no publish) | CI build + tests + DocFX |
| `beta` | `{VersionPrefix}-beta` from [EventHook.csproj](src/EventHook/EventHook.csproj) | Merge `develop` → `beta` to publish prerelease |
| `stable` / tag `v*` | `{VersionPrefix}` from csproj | Stable release + GitHub Pages docs |

Publishing uses NuGet Trusted Publishing (`NUGET_USER` on the `nuget-publish` environment), same pattern as titanium-web-proxy.

## Version 2.0 notes

Breaking: targets `net10.0-windows` only (no longer .NET Framework 4.5).

Highlights: HotkeyWatcher, mouse-move filter, clipboard images/files, dialog/MsgBox tracking, PDF/virtual printers, x64/ARM64 P/Invoke fixes, reliable Stop/Dispose, GitHub Actions CI + DocFX.
