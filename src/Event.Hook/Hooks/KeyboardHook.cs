using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace EventHook.Hooks
{
    [InlineArray(256)]
    internal struct KeyboardStateBuffer
    {
        private byte element0;
    }

    /// <summary>
    /// Value-type keyboard event captured on the low-level hook thread (no decode).
    /// </summary>
    internal struct KeyboardSnapshot
    {
        internal int VkCode;
        internal int ScanCode;
        internal int Flags;
        internal int Time;
        internal uint WParam;
        internal IntPtr KeyboardLayout;
        private KeyboardStateBuffer keyState;

        internal void SetKeyState(ReadOnlySpan<byte> source)
        {
            var copy = source.Length < 256 ? source.Length : 256;
            for (var i = 0; i < copy; i++)
            {
                keyState[i] = source[i];
            }

            for (var i = copy; i < 256; i++)
            {
                keyState[i] = 0;
            }
        }

        internal void CopyKeyStateTo(byte[] destination)
        {
            if (destination == null || destination.Length < 256)
            {
                throw new ArgumentException("Key state buffer must be at least 256 bytes.", nameof(destination));
            }

            for (var i = 0; i < 256; i++)
            {
                destination[i] = keyState[i];
            }
        }

        internal bool IsKeyDown =>
            WParam == (uint)KeyEvent.WM_KEYDOWN || WParam == (uint)KeyEvent.WM_SYSKEYDOWN;

        internal bool IsSysKey =>
            WParam == (uint)KeyEvent.WM_SYSKEYDOWN || WParam == (uint)KeyEvent.WM_SYSKEYUP;

        internal int EventType => IsKeyDown ? 0 : 1;

        internal enum KeyEvent : uint
        {
            WM_KEYDOWN = 256,
            WM_KEYUP = 257,
            WM_SYSKEYDOWN = 260,
            WM_SYSKEYUP = 261
        }
    }

    /// <summary>
    /// Low-level keyboard hook. Callback only copies a <see cref="KeyboardSnapshot"/> and invokes the enqueue action.
    /// </summary>
    internal sealed class KeyboardHook : IDisposable
    {
        private const int WhKeyboardLl = 13;
        private IntPtr hookId = IntPtr.Zero;
        private LowLevelKeyboardProc proc;
        private Action<KeyboardSnapshot> onSnapshot;
        private bool disposed;

        internal HookStartResult Start(Action<KeyboardSnapshot> enqueueSnapshot)
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
            hookId = SetHook(proc);
            if (hookId == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                onSnapshot = null;
                proc = null;
                return HookStartResult.Fail(
                    HookFailureReason.NativeFailure,
                    $"SetWindowsHookEx(WH_KEYBOARD_LL) failed. Win32 error: {error}.");
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

        [MethodImpl(MethodImplOptions.NoInlining)]
        private IntPtr HookCallback(int nCode, UIntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var message = wParam.ToUInt32();
                if (message == (uint)KeyboardSnapshot.KeyEvent.WM_KEYDOWN ||
                    message == (uint)KeyboardSnapshot.KeyEvent.WM_KEYUP ||
                    message == (uint)KeyboardSnapshot.KeyEvent.WM_SYSKEYDOWN ||
                    message == (uint)KeyboardSnapshot.KeyEvent.WM_SYSKEYUP)
                {
                    var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                    var foreground = GetForegroundWindow();
                    uint processId;
                    var layoutThread = GetWindowThreadProcessId(foreground, out processId);

                    var snapshot = new KeyboardSnapshot
                    {
                        VkCode = (int)info.vkCode,
                        ScanCode = (int)info.scanCode,
                        Flags = (int)info.flags,
                        Time = (int)info.time,
                        WParam = message,
                        KeyboardLayout = GetKeyboardLayout(layoutThread)
                    };

                    unsafe // NOSONAR S6640 - stackalloc required; hook callback must not allocate
                    {
                        byte* state = stackalloc byte[256];
                        if (GetKeyboardState((IntPtr)state))
                        {
                            snapshot.SetKeyState(new ReadOnlySpan<byte>(state, 256));
                        }
                    }

                    try
                    {
                        onSnapshot?.Invoke(snapshot);
                    }
                    catch
                    {
                        // never throw from hook callback
                    }
                }
            }

            return CallNextHookEx(hookId, nCode, wParam, lParam);
        }

        /// <summary>
        /// Decode Unicode for a captured snapshot. Call only from the consumer thread — never from the hook.
        /// </summary>
        internal static string VkCodeToString(in KeyboardSnapshot snapshot, bool isKeyDown)
        {
            if (!isKeyDown)
            {
                return string.Empty;
            }

            var sbString = new StringBuilder(5);
            var bKeyState = new byte[256];
            snapshot.CopyKeyStateTo(bKeyState);

            var hkl = snapshot.KeyboardLayout;
            if (hkl == IntPtr.Zero)
            {
                hkl = GetKeyboardLayout(0);
            }

            var vkCode = (uint)snapshot.VkCode;
            var lScanCode = MapVirtualKeyEx(vkCode, 0, hkl);
            if (lScanCode == 0 && snapshot.ScanCode != 0)
            {
                lScanCode = (uint)snapshot.ScanCode;
            }

            var relevantKeyCountInBuffer =
                ToUnicodeEx(vkCode, lScanCode, bKeyState, sbString, sbString.Capacity, 0, hkl);

            switch (relevantKeyCountInBuffer)
            {
                case -1:
                    ClearKeyboardBuffer(vkCode, lScanCode, hkl);
                    return string.Empty;
                case 0:
                    return string.Empty;
                case 1:
                    return sbString[0].ToString();
                default:
                    return sbString.ToString().Substring(0, Math.Min(2, sbString.Length));
            }
        }

        private static void ClearKeyboardBuffer(uint vk, uint sc, IntPtr hkl)
        {
            var sb = new StringBuilder(10);
            var lpKeyStateNull = new byte[256];
            int rc;
            do
            {
                rc = ToUnicodeEx(vk, sc, lpKeyStateNull, sb, sb.Capacity, 0, hkl);
            } while (rc < 0);
        }

        private static IntPtr SetHook(LowLevelKeyboardProc callback)
        {
            using (var curProcess = Process.GetCurrentProcess())
            using (var curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WhKeyboardLl, callback, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            internal uint vkCode;
            internal uint scanCode;
            internal uint flags;
            internal uint time;
            internal UIntPtr dwExtraInfo;
        }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, UIntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod,
            uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, UIntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern bool GetKeyboardState(IntPtr lpKeyState);

        [DllImport("user32.dll")]
        private static extern IntPtr GetKeyboardLayout(uint idThread);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKeyEx(uint uCode, uint uMapType, IntPtr dwhkl);

        [DllImport("user32.dll")]
        private static extern int ToUnicodeEx(uint wVirtKey, uint wScanCode, byte[] lpKeyState,
            [Out] [MarshalAs(UnmanagedType.LPWStr)] StringBuilder pwszBuff, int cchBuff, uint wFlags, IntPtr dwhkl);
    }
}
