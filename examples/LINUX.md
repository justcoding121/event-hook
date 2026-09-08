# Linux setup (EventHook)

## X11 (recommended for v3)

Install runtime libraries, then run under a display (or Xvfb):

```bash
sudo apt-get install -y libx11-6 libxext6 libxfixes3 libxtst6 libcups2 xclip xvfb
dotnet run --project examples/Event.Hook.ConsoleApp.Example -f net10.0
# or headless:
xvfb-run -a dotnet run --project examples/Event.Hook.ConsoleApp.Example -f net10.0
```

Requires `DISPLAY`. Keyboard, mouse, clipboard, windows, and hotkeys use X11 APIs.

## Wayland / no X11

- Keyboard/mouse can use `/dev/input/event*` (evdev). If open fails with permission denied:

```bash
sudo usermod -aG input "$USER"
# then log out and back in
```

- Clipboard, application windows, and hotkeys return `NotSupportedOnPlatform` without X11 in EventHook v3.

## Print

Uses CUPS (`libcups`). Missing library or scheduler → `NativeFailure`.
