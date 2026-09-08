using System;
using System.Runtime.InteropServices;

namespace EventHook.Hooks
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct MSLLHOOKSTRUCT
    {
        internal Point pt;
        internal readonly uint mouseData;
        internal readonly uint flags;
        internal readonly uint time;
        internal readonly IntPtr dwExtraInfo;
    }

    /// <summary>
    /// Low-level mouse hook. Callback only copies a <see cref="MouseSnapshot"/> and invokes the enqueue action.
    /// </summary>
    internal sealed class MouseHook : IDisposable
    {
        private const int WhMouseLl = 14;
        private IntPtr hookId = IntPtr.Zero;
        private LowLevelMouseProc proc;
        private Action<MouseSnapshot> onSnapshot;
        private bool disposed;

        internal HookStartResult Start(Action<MouseSnapshot> enqueueSnapshot)
        {
            if (enqueueSnapshot == null)
            {
                throw new ArgumentNullException(nameof(enqueueSnapshot));
            }

            if (hookId != IntPtr.Zero)
            {
                return HookStartResult.Ok();
            }

            onSnapshot = enqueueSnapshot;
            proc = HookCallback;
            hookId = SetWindowsHookEx(WhMouseLl, proc, GetModuleHandle("user32"), 0);
            if (hookId == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                onSnapshot = null;
                proc = null;
                return HookStartResult.Fail(
                    HookFailureReason.NativeFailure,
                    $"SetWindowsHookEx(WH_MOUSE_LL) failed. Win32 error: {error}.");
            }

            return HookStartResult.Ok();
        }

        internal void Stop()
        {
            if (hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(hookId);
                hookId = IntPtr.Zero;
            }

            onSnapshot = null;
            proc = null;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Stop();
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                var snapshot = new MouseSnapshot(
                    (MouseMessages)wParam,
                    hookStruct.pt,
                    hookStruct.mouseData);

                try
                {
                    onSnapshot?.Invoke(snapshot);
                }
                catch
                {
                    // never throw from hook callback
                }
            }

            return CallNextHookEx(hookId, nCode, wParam, lParam);
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
    }
}
