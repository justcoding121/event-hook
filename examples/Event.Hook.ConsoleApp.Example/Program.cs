using System;

namespace EventHook.ConsoleApp.Example
{
    internal static class Program
    {
        private static void Main()
        {
            using var eventHookFactory = new EventHookFactory();

            var keyboardWatcher = eventHookFactory.GetKeyboardWatcher();
            Console.WriteLine("Keyboard: {0}", keyboardWatcher.Start());
            keyboardWatcher.OnKeyInput += (_, e) =>
                Console.WriteLine("Key {0} event of key {1}", e.KeyData.EventType, e.KeyData.Keyname);

            var mouseWatcher = eventHookFactory.GetMouseWatcher();
            mouseWatcher.IncludeMouseMove = false;
            Console.WriteLine("Mouse: {0}", mouseWatcher.Start());
            mouseWatcher.OnMouseInput += (_, e) =>
                Console.WriteLine("Mouse event {0} at point {1},{2}", e.Message, e.Point.x, e.Point.y);

            var clipboardWatcher = eventHookFactory.GetClipboardWatcher();
            Console.WriteLine("Clipboard: {0}", clipboardWatcher.Start());
            clipboardWatcher.OnClipboardModified += (_, e) =>
                Console.WriteLine("Clipboard updated with data '{0}' of format {1}", e.Data, e.DataFormat);

            var applicationWatcher = eventHookFactory.GetApplicationWatcher();
            Console.WriteLine("Application: {0}", applicationWatcher.Start());
            applicationWatcher.OnApplicationWindowChange += (_, e) =>
                Console.WriteLine("Application window of '{0}' with the title '{1}' was {2}",
                    e.ApplicationData.AppName, e.ApplicationData.AppTitle, e.Event);

            var printWatcher = eventHookFactory.GetPrintWatcher();
            Console.WriteLine("Print: {0}", printWatcher.Start());
            printWatcher.OnPrintEvent += (_, e) =>
                Console.WriteLine("Printer '{0}' currently printing {1} pages.", e.EventData.PrinterName,
                    e.EventData.Pages);

            var hotkeyWatcher = eventHookFactory.GetHotkeyWatcher();
            Console.WriteLine("Hotkey Start: {0}", hotkeyWatcher.Start());
            Console.WriteLine("Hotkey Register: {0}",
                hotkeyWatcher.Register("demo", new Hotkey(KeyModifiers.Control | KeyModifiers.Alt, EventKey.H)));
            hotkeyWatcher.OnHotkeyPressed += (_, e) =>
                Console.WriteLine("Hotkey {0} ({1}) pressed", e.Id, e.Hotkey);

            Console.WriteLine("Watching... press Enter to stop.");
            Console.ReadLine();

            keyboardWatcher.Stop();
            mouseWatcher.Stop();
            clipboardWatcher.Stop();
            applicationWatcher.Stop();
            printWatcher.Stop();
            hotkeyWatcher.Stop();
        }
    }
}
