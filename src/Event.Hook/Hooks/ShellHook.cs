using System;
using System.Windows.Forms;
using EventHook.Helpers;
using EventHook.Hooks.Library;

namespace EventHook.Hooks
{
    internal delegate void GeneralShellHookEventHandler(ShellHook sender, IntPtr hWnd);

    internal sealed class ShellHook : NativeWindow
    {
        private readonly uint _wmShellHook;

        internal ShellHook(IntPtr hWnd)
        {
            CreateHandle(new CreateParams());

            User32.SetTaskmanWindow(hWnd);

            if (User32.RegisterShellHookWindow(Handle))
            {
                _wmShellHook = User32.RegisterWindowMessage("SHELLHOOK");
            }
        }

        internal void DeRegister()
        {
            User32.RegisterShellHook(Handle, 0);
            DestroyHandle();
        }

        internal event GeneralShellHookEventHandler WindowCreated;
        internal event GeneralShellHookEventHandler WindowDestroyed;
        internal event GeneralShellHookEventHandler WindowActivated;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == _wmShellHook)
            {
                switch ((ShellEvents)m.WParam)
                {
                    case ShellEvents.HSHELL_WINDOWCREATED:
                        if (AppWindowFilter.IsAppWindow(m.LParam))
                        {
                            WindowCreated?.Invoke(this, m.LParam);
                        }
                        break;
                    case ShellEvents.HSHELL_WINDOWDESTROYED:
                        WindowDestroyed?.Invoke(this, m.LParam);
                        break;
                    case ShellEvents.HSHELL_WINDOWACTIVATED:
                    case ShellEvents.HSHELL_RUDEAPPACTIVATED:
                        WindowActivated?.Invoke(this, m.LParam);
                        break;
                }
            }

            base.WndProc(ref m);
        }

        internal void EnumWindows()
        {
            User32.EnumWindows(EnumWindowsProc, IntPtr.Zero);
        }

        private bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam)
        {
            if (AppWindowFilter.IsAppWindow(hWnd))
            {
                WindowCreated?.Invoke(this, hWnd);
            }

            return true;
        }
    }
}
