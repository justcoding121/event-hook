using System;
using System.Windows.Forms;
using EventHook.Hooks.Library;

// https://github.com/MrksKwsnck/Wlipper
namespace EventHook.Hooks
{
    internal class ClipBoardHook : Form
    {
        private IntPtr _clipboardViewerNext;
        private bool _usingFormatListener;

        internal event EventHandler ClipBoardChanged = delegate { };

        /// <summary>
        ///     Register this form as a clipboard listener (modern API, with viewer-chain fallback).
        /// </summary>
        internal void RegisterClipboardViewer()
        {
            _usingFormatListener = User32.AddClipboardFormatListener(Handle);
            if (!_usingFormatListener)
            {
                _clipboardViewerNext = User32.SetClipboardViewer(Handle);
            }
        }

        /// <summary>
        ///     Remove this form from clipboard notifications.
        /// </summary>
        internal void UnregisterClipboardViewer()
        {
            if (_usingFormatListener)
            {
                User32.RemoveClipboardFormatListener(Handle);
                _usingFormatListener = false;
                return;
            }

            User32.ChangeClipboardChain(Handle, _clipboardViewerNext);
        }


        /// <summary>
        ///     Show the clipboard contents in the window
        ///     and show the notification balloon if a link is found
        /// </summary>
        private void GetClipboardData()
        {
            try
            {
                // Already on the STA pump thread — do not Join another STA worker
                // from WndProc (that deadlocks OLE clipboard messages).
                var iData = Clipboard.GetDataObject();
                ClipBoardChanged(iData, new EventArgs());
            }
            catch
            {
                // clipboard can be locked by the writer
            }
        }


        protected override void WndProc(ref Message m)
        {
            switch ((Msgs)m.Msg)
            {
                //
                // The WM_DRAWCLIPBOARD message is sent to the first window 
                // in the clipboard viewer chain when the content of the 
                // clipboard changes. This enables a clipboard viewer 
                // window to display the new content of the clipboard. 
                //
                case Msgs.WM_CLIPBOARDUPDATE:
                    GetClipboardData();
                    break;

                case Msgs.WM_DRAWCLIPBOARD:


                    GetClipboardData();

                    //
                    // Each window that receives the WM_DRAWCLIPBOARD message 
                    // must call the SendMessage function to pass the message 
                    // on to the next window in the clipboard viewer chain.
                    //
                    User32.SendMessage(_clipboardViewerNext, m.Msg, m.WParam, m.LParam);
                    break;


                //
                // The WM_CHANGECBCHAIN message is sent to the first window 
                // in the clipboard viewer chain when a window is being 
                // removed from the chain. 
                //
                case Msgs.WM_CHANGECBCHAIN:


                    // When a clipboard viewer window receives the WM_CHANGECBCHAIN message, 
                    // it should call the SendMessage function to pass the message to the 
                    // next window in the chain, unless the next window is the window 
                    // being removed. In this case, the clipboard viewer should save 
                    // the handle specified by the lParam parameter as the next window in the chain. 

                    //
                    // wParam is the Handle to the window being removed from 
                    // the clipboard viewer chain 
                    // lParam is the Handle to the next window in the chain 
                    // following the window being removed. 
                    if (m.WParam == _clipboardViewerNext)
                    {
                        //
                        // If wParam is the next clipboard viewer then it
                        // is being removed so update pointer to the next
                        // window in the clipboard chain
                        //
                        _clipboardViewerNext = m.LParam;
                    }
                    else
                    {
                        User32.SendMessage(_clipboardViewerNext, m.Msg, m.WParam, m.LParam);
                    }

                    break;

                default:
                    //
                    // Let the form process the messages that we are
                    // not interested in
                    //
                    base.WndProc(ref m);
                    break;
            }
        }
    }
}
