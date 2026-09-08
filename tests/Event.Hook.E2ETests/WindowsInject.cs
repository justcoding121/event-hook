#if WINDOWS
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace EventHook.E2ETests
{
    /// <summary>
    /// Injects SendInput events so Windows e2e can verify low-level hook delivery.
    /// </summary>
    internal static class WindowsInject
    {
        internal const ushort VkF24 = 0x87;
        internal const ushort VkF10 = 0x79;
        internal const ushort VkF11 = 0x7A;
        internal const ushort VkControl = 0x11;
        internal const ushort VkMenu = 0x12;
        internal const ushort VkShift = 0x10;

        internal static bool TryFakeKey(ushort vk)
        {
            var inputs = new[]
            {
                Key(vk, keyUp: false),
                Key(vk, keyUp: true)
            };
            return Send(inputs);
        }

        internal static bool TryFakeKeyDown(ushort vk) => Send(new[] { Key(vk, keyUp: false) });

        internal static bool TryFakeKeyUp(ushort vk) => Send(new[] { Key(vk, keyUp: true) });

        internal static bool TryFakeLeftClickAt(int screenX, int screenY)
        {
            var bounds = SystemInformation.VirtualScreen;
            var w = Math.Max(1, bounds.Width);
            var h = Math.Max(1, bounds.Height);
            var absX = (int)Math.Round((screenX - bounds.Left) * 65535.0 / Math.Max(1, w - 1));
            var absY = (int)Math.Round((screenY - bounds.Top) * 65535.0 / Math.Max(1, h - 1));
            const uint flags = MouseeventfMove | MouseeventfAbsolute | MouseeventfVirtualdesk;
            var inputs = new[]
            {
                Mouse(flags, absX, absY),
                Mouse(MouseeventfLeftdown | MouseeventfAbsolute | MouseeventfVirtualdesk, absX, absY),
                Mouse(MouseeventfLeftup | MouseeventfAbsolute | MouseeventfVirtualdesk, absX, absY)
            };
            return Send(inputs);
        }

        internal static bool TryFakeHotkeyCtrlAltShift(ushort vk)
        {
            if (!TryFakeKeyDown(VkControl) || !TryFakeKeyDown(VkMenu) || !TryFakeKeyDown(VkShift))
            {
                TryFakeKeyUp(VkShift);
                TryFakeKeyUp(VkMenu);
                TryFakeKeyUp(VkControl);
                return false;
            }

            Thread.Sleep(40);
            var tapped = TryFakeKey(vk);
            Thread.Sleep(40);
            TryFakeKeyUp(VkShift);
            TryFakeKeyUp(VkMenu);
            TryFakeKeyUp(VkControl);
            return tapped;
        }

        internal static bool TryGetCursorPos(out Point point)
        {
            return GetCursorPos(out point);
        }

        internal static void RestoreCursor(int x, int y)
        {
            SetCursorPos(x, y);
        }

        private static bool Send(INPUT[] inputs)
        {
            return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == (uint)inputs.Length;
        }

        private static INPUT Key(ushort vk, bool keyUp)
        {
            var scan = (ushort)MapVirtualKey(vk, 0);
            var flags = keyUp ? KeyeventfKeyup : 0u;
            if (vk is >= 0x70 and <= 0x87)
            {
                flags |= KeyeventfExtendedkey;
            }

            return new INPUT
            {
                type = InputKeyboard,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = vk,
                        wScan = scan,
                        dwFlags = flags,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
        }

        private static INPUT Mouse(uint flags, int x, int y)
        {
            return new INPUT
            {
                type = InputMouse,
                U = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dx = x,
                        dy = y,
                        mouseData = 0,
                        dwFlags = flags,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
        }

        private const uint InputMouse = 0;
        private const uint InputKeyboard = 1;
        private const uint KeyeventfExtendedkey = 0x0001;
        private const uint KeyeventfKeyup = 0x0002;
        private const uint MouseeventfMove = 0x0001;
        private const uint MouseeventfLeftdown = 0x0002;
        private const uint MouseeventfLeftup = 0x0004;
        private const uint MouseeventfAbsolute = 0x8000;
        private const uint MouseeventfVirtualdesk = 0x4000;

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            internal uint type;
            internal InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)]
            internal MOUSEINPUT mi;
            [FieldOffset(0)]
            internal KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            internal int dx;
            internal int dy;
            internal uint mouseData;
            internal uint dwFlags;
            internal uint time;
            internal IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            internal ushort wVk;
            internal ushort wScan;
            internal uint dwFlags;
            internal uint time;
            internal IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out Point lpPoint);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);
    }
}
#endif
