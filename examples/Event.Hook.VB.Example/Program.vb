Imports System
Imports System.Windows.Forms
Imports EventHook

Module Program
    Sub Main()
        Using factory As New EventHookFactory()
            Dim keyboardWatcher = factory.GetKeyboardWatcher()
            keyboardWatcher.Start()
            AddHandler keyboardWatcher.OnKeyInput, AddressOf OnKeyInput

            Dim clipboardWatcher = factory.GetClipboardWatcher()
            clipboardWatcher.Start()
            AddHandler clipboardWatcher.OnClipboardModified, AddressOf OnClipboard

            Console.WriteLine("VB EventHook sample running. Paste or type keys. Press Enter to exit.")
            Console.ReadLine()

            keyboardWatcher.Stop()
            clipboardWatcher.Stop()
        End Using
    End Sub

    Private Sub OnKeyInput(sender As Object, e As KeyInputEventArgs)
        Console.WriteLine($"Key {e.KeyData.EventType} of {e.KeyData.Keyname}")
    End Sub

    Private Sub OnClipboard(sender As Object, e As ClipboardEventArgs)
        ' Context-menu paste updates the clipboard and is visible here (WM_PASTE is app-local).
        Console.WriteLine($"Clipboard {e.DataFormat}: {e.Data}")
    End Sub
End Module
