# EventHook

A .NET 10 Windows library for global keyboard, mouse, clipboard, application, print, and hotkey events.

## Install

```bash
dotnet add package EventHook
```

## Quick start

```csharp
using (var factory = new EventHookFactory())
{
    var keyboard = factory.GetKeyboardWatcher();
    keyboard.Start();
    keyboard.OnKeyInput += (_, e) => Console.WriteLine(e.KeyData.Keyname);
}
```

See the [GitHub README](https://github.com/justcoding121/windows-user-action-hook) for full samples.
