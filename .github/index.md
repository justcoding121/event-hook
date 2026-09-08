# EventHook

A .NET 10 library for global keyboard, mouse, clipboard, application, print, and hotkey events on Windows, macOS, and Linux.

## Install

```bash
dotnet add package EventHook
```

## Quick start

```csharp
using (var factory = new EventHookFactory())
{
    var keyboard = factory.GetKeyboardWatcher();
    var result = keyboard.Start();
    if (result.Success)
    {
        keyboard.OnKeyInput += (_, e) => Console.WriteLine(e.KeyData.Keyname);
    }
    else
    {
        Console.WriteLine(result);
    }
}
```

See the [GitHub README](https://github.com/justcoding121/event-hook) for full samples.
