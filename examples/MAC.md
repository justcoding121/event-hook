# macOS setup (EventHook)

Grant privacy permissions to the **host process** that loads EventHook (`dotnet`, Terminal, or your signed `.app`) — not to the DLL itself.

## Permissions

| Feature | Permission | Location |
|---|---|---|
| Keyboard / mouse | Input Monitoring | System Settings → Privacy & Security → Input Monitoring |
| Window title / minimize (`WindowHookEx`) | Accessibility | System Settings → Privacy & Security → Accessibility |
| Hotkeys (`RegisterEventHotKey`) | None | — |
| Clipboard / app launch-activate / CUPS print | None (general) | — |

After changing TCC grants, restart the host process.

## Run the console example

```bash
dotnet run --project examples/Event.Hook.ConsoleApp.Example -f net10.0
```

Each watcher prints a `HookStartResult`. A `PermissionDenied` message names the missing TCC service.
