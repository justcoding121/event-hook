using System;
using System.Windows;

namespace EventHook.WPF.Example
{
    /// <summary>
    ///     Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly ApplicationWatcher applicationWatcher;
        private readonly ClipboardWatcher clipboardWatcher;
        private readonly EventHookFactory eventHookFactory = new EventHookFactory();
        private readonly KeyboardWatcher keyboardWatcher;
        private readonly MouseWatcher mouseWatcher;
        private readonly PrintWatcher printWatcher;

        public MainWindow()
        {
            InitializeComponent();

            Application.Current.Exit += OnApplicationExit;

            keyboardWatcher = eventHookFactory.GetKeyboardWatcher();
            keyboardWatcher.Start();
            keyboardWatcher.OnKeyInput += (s, e) =>
            {
                Console.WriteLine("Key {0} event of key {1}", e.KeyData.EventType, e.KeyData.Keyname);
            };

            mouseWatcher = eventHookFactory.GetMouseWatcher();
            mouseWatcher.Start();
            mouseWatcher.OnMouseInput += (s, e) =>
            {
                Console.WriteLine("Mouse event {0} at point {1},{2}", e.Message.ToString(), e.Point.x, e.Point.y);
            };

            clipboardWatcher = eventHookFactory.GetClipboardWatcher();
            clipboardWatcher.Start();
            clipboardWatcher.OnClipboardModified += (s, e) =>
            {
                Console.WriteLine("Clipboard updated with data '{0}' of format {1}", e.Data,
                    e.DataFormat.ToString());
            };


            applicationWatcher = eventHookFactory.GetApplicationWatcher();
            applicationWatcher.Start();
            applicationWatcher.OnApplicationWindowChange += (s, e) =>
            {
                Console.WriteLine("Application window of '{0}' with the title '{1}' was {2}",
                    e.ApplicationData.AppName, e.ApplicationData.AppTitle, e.Event);
            };

            printWatcher = eventHookFactory.GetPrintWatcher();
            printWatcher.Start();
            printWatcher.OnPrintEvent += (s, e) =>
            {
                Console.WriteLine("Printer '{0}' currently printing {1} pages.", e.EventData.PrinterName,
                    e.EventData.Pages);
            };
            eventHookFactory.Dispose();
        }

        private void OnApplicationExit(object sender, EventArgs e)
        {
            keyboardWatcher.Stop();
            mouseWatcher.Stop();
            clipboardWatcher.Stop();
            applicationWatcher.Stop();
            printWatcher.Stop();

            eventHookFactory.Dispose();
        }
    }
}
