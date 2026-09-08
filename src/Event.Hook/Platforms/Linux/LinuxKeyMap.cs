using System;
using System.Text;

namespace EventHook.Platforms.Linux
{
    /// <summary>
    /// Maps X11 KeySym / Linux evdev codes onto Win32-like virtual-key values and names.
    /// </summary>
    internal static class LinuxKeyMap
    {
        // X11 keysyms (latin-1 + common specials)
        private const ulong XK_BackSpace = 0xff08;
        private const ulong XK_Tab = 0xff09;
        private const ulong XK_Return = 0xff0d;
        private const ulong XK_Escape = 0xff1b;
        private const ulong XK_Delete = 0xffff;
        private const ulong XK_Home = 0xff50;
        private const ulong XK_Left = 0xff51;
        private const ulong XK_Up = 0xff52;
        private const ulong XK_Right = 0xff53;
        private const ulong XK_Down = 0xff54;
        private const ulong XK_Page_Up = 0xff55;
        private const ulong XK_Page_Down = 0xff56;
        private const ulong XK_End = 0xff57;
        private const ulong XK_Insert = 0xff63;
        private const ulong XK_Shift_L = 0xffe1;
        private const ulong XK_Shift_R = 0xffe2;
        private const ulong XK_Control_L = 0xffe3;
        private const ulong XK_Control_R = 0xffe4;
        private const ulong XK_Alt_L = 0xffe9;
        private const ulong XK_Alt_R = 0xffea;
        private const ulong XK_Super_L = 0xffeb;
        private const ulong XK_Super_R = 0xffec;
        private const ulong XK_space = 0x0020;
        private const ulong XK_F1 = 0xffbe;

        internal static int KeySymToVk(ulong keysym)
        {
            if (keysym >= 0x20 && keysym <= 0x7e)
            {
                if (keysym >= 'a' && keysym <= 'z')
                {
                    return (int)(keysym - 32); // A-Z
                }

                return (int)keysym;
            }

            switch (keysym)
            {
                case XK_BackSpace: return 0x08;
                case XK_Tab: return 0x09;
                case XK_Return: return 0x0D;
                case XK_Escape: return 0x1B;
                case XK_space: return 0x20;
                case XK_Page_Up: return 0x21;
                case XK_Page_Down: return 0x22;
                case XK_End: return 0x23;
                case XK_Home: return 0x24;
                case XK_Left: return 0x25;
                case XK_Up: return 0x26;
                case XK_Right: return 0x27;
                case XK_Down: return 0x28;
                case XK_Insert: return 0x2D;
                case XK_Delete: return 0x2E;
                case XK_Shift_L: return 0xA0;
                case XK_Shift_R: return 0xA1;
                case XK_Control_L: return 0xA2;
                case XK_Control_R: return 0xA3;
                case XK_Alt_L: return 0xA4;
                case XK_Alt_R: return 0xA5;
                case XK_Super_L: return 0x5B;
                case XK_Super_R: return 0x5C;
                default:
                    if (keysym >= XK_F1 && keysym <= XK_F1 + 11)
                    {
                        return 0x70 + (int)(keysym - XK_F1);
                    }

                    return (int)(keysym & 0xFF);
            }
        }

        internal static string KeySymToUnicode(ulong keysym)
        {
            if (keysym >= 0x20 && keysym <= 0x7e)
            {
                return ((char)keysym).ToString();
            }

            if (keysym == XK_space)
            {
                return " ";
            }

            if (keysym == XK_Tab)
            {
                return "\t";
            }

            if (keysym == XK_Return)
            {
                return "\r";
            }

            // Latin-1 KeySyms match Unicode code points in 0xa0–0xff.
            if (keysym >= 0xa0 && keysym <= 0xff)
            {
                return ((char)keysym).ToString();
            }

            return string.Empty;
        }

        internal static ulong EventKeyToKeySym(EventKey key)
        {
            switch (key)
            {
                case EventKey.Back: return XK_BackSpace;
                case EventKey.Tab: return XK_Tab;
                case EventKey.Return: return XK_Return;
                case EventKey.Escape: return XK_Escape;
                case EventKey.Space: return XK_space;
                case EventKey.PageUp: return XK_Page_Up;
                case EventKey.PageDown: return XK_Page_Down;
                case EventKey.End: return XK_End;
                case EventKey.Home: return XK_Home;
                case EventKey.Left: return XK_Left;
                case EventKey.Up: return XK_Up;
                case EventKey.Right: return XK_Right;
                case EventKey.Down: return XK_Down;
                case EventKey.Insert: return XK_Insert;
                case EventKey.Delete: return XK_Delete;
                case EventKey.F1: return XK_F1;
                case EventKey.F2: return XK_F1 + 1;
                case EventKey.F3: return XK_F1 + 2;
                case EventKey.F4: return XK_F1 + 3;
                case EventKey.F5: return XK_F1 + 4;
                case EventKey.F6: return XK_F1 + 5;
                case EventKey.F7: return XK_F1 + 6;
                case EventKey.F8: return XK_F1 + 7;
                case EventKey.F9: return XK_F1 + 8;
                case EventKey.F10: return XK_F1 + 9;
                case EventKey.F11: return XK_F1 + 10;
                case EventKey.F12: return XK_F1 + 11;
                default:
                    if (key >= EventKey.D0 && key <= EventKey.D9)
                    {
                        return (ulong)('0' + (key - EventKey.D0));
                    }

                    if (key >= EventKey.A && key <= EventKey.Z)
                    {
                        return (ulong)('A' + (key - EventKey.A));
                    }

                    return 0;
            }
        }

        internal static uint ModifiersToX(KeyModifiers modifiers)
        {
            uint mask = 0;
            if (modifiers.HasFlag(KeyModifiers.Shift))
            {
                mask |= LinuxX11Native.ShiftMask;
            }

            if (modifiers.HasFlag(KeyModifiers.Control))
            {
                mask |= LinuxX11Native.ControlMask;
            }

            if (modifiers.HasFlag(KeyModifiers.Alt))
            {
                mask |= LinuxX11Native.Mod1Mask;
            }

            if (modifiers.HasFlag(KeyModifiers.Meta))
            {
                mask |= LinuxX11Native.Mod4Mask;
            }

            return mask;
        }

        // Linux input event codes (input-event-codes.h)
        internal const ushort EvKey = 1;
        internal const ushort EvRel = 2;
        internal const ushort EvAbs = 3;
        internal const ushort EvSyn = 0;

        internal const ushort RelX = 0;
        internal const ushort RelY = 1;
        internal const ushort RelWheel = 8;
        internal const ushort RelHWheel = 6;

        internal const ushort BtnLeft = 0x110;
        internal const ushort BtnRight = 0x111;
        internal const ushort BtnMiddle = 0x112;
        internal const ushort BtnSide = 0x113;
        internal const ushort BtnExtra = 0x114;

        internal static int EvdevKeyToVk(ushort code)
        {
            // KEY_* codes largely align with USB HID; map common set to VK.
            switch (code)
            {
                case 1: return 0x1B; // ESC
                case 14: return 0x08; // BACKSPACE
                case 15: return 0x09; // TAB
                case 28: return 0x0D; // ENTER
                case 57: return 0x20; // SPACE
                case 102: return 0x24; // HOME
                case 107: return 0x23; // END
                case 104: return 0x21; // PAGEUP
                case 109: return 0x22; // PAGEDOWN
                case 103: return 0x26; // UP
                case 108: return 0x28; // DOWN
                case 105: return 0x25; // LEFT
                case 106: return 0x27; // RIGHT
                case 110: return 0x2D; // INSERT
                case 111: return 0x2E; // DELETE
                case 42: return 0xA0; // LEFTSHIFT
                case 54: return 0xA1; // RIGHTSHIFT
                case 29: return 0xA2; // LEFTCTRL
                case 97: return 0xA3; // RIGHTCTRL
                case 56: return 0xA4; // LEFTALT
                case 100: return 0xA5; // RIGHTALT
                case 125: return 0x5B; // LEFTMETA
                case 126: return 0x5C; // RIGHTMETA
                default:
                    if (code >= 2 && code <= 11)
                    {
                        // KEY_1..KEY_0
                        return code == 11 ? 0x30 : 0x30 + (code - 1);
                    }

                    if (code >= 16 && code <= 25)
                    {
                        // Q-P row approximate
                        return "QWERTYUIOP"[code - 16];
                    }

                    if (code >= 30 && code <= 38)
                    {
                        return "ASDFGHJKL"[code - 30];
                    }

                    if (code >= 44 && code <= 50)
                    {
                        return "ZXCVBNM"[code - 44];
                    }

                    if (code >= 59 && code <= 68)
                    {
                        return 0x70 + (code - 59); // F1-F10
                    }

                    if (code == 87)
                    {
                        return 0x7A; // F11
                    }

                    if (code == 88)
                    {
                        return 0x7B; // F12
                    }

                    return code;
            }
        }

        internal static string EvdevKeyToUnicode(ushort code, int value)
        {
            if (value != 1)
            {
                return string.Empty;
            }

            if (code >= 2 && code <= 11)
            {
                var ch = code == 11 ? '0' : (char)('1' + (code - 2));
                return ch.ToString();
            }

            var vk = EvdevKeyToVk(code);
            if (vk >= 'A' && vk <= 'Z')
            {
                return ((char)(vk + 32)).ToString();
            }

            if (vk == 0x20)
            {
                return " ";
            }

            return string.Empty;
        }
    }

    /// <summary>
    /// POD keyboard snapshot for Linux backends (XRecord / evdev).
    /// </summary>
    internal readonly struct LinuxKeySnapshot
    {
        internal LinuxKeySnapshot(int vkCode, int eventType, ulong keySym)
        {
            VkCode = vkCode;
            EventType = eventType;
            KeySym = keySym;
        }

        internal int VkCode { get; }
        /// <summary>0 = down, 1 = up.</summary>
        internal int EventType { get; }
        internal ulong KeySym { get; }
    }

    /// <summary>
    /// Lightweight window event for Linux application watcher callbacks.
    /// </summary>
    internal readonly struct LinuxWindowSnapshot
    {
        internal LinuxWindowSnapshot(IntPtr hwnd, int eventType)
        {
            HWnd = hwnd;
            EventType = eventType;
        }

        internal IntPtr HWnd { get; }
        /// <summary>0 = created, 1 = activated, 2 = destroyed.</summary>
        internal int EventType { get; }
    }

    /// <summary>
    /// Clipboard change token; payload read on the consumer.
    /// </summary>
    internal readonly struct LinuxClipboardSnapshot
    {
        internal LinuxClipboardSnapshot(int generation)
        {
            Generation = generation;
        }

        internal int Generation { get; }
    }
}
