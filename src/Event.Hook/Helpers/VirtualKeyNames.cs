namespace EventHook.Helpers
{
    /// <summary>
    /// Maps Win32 virtual-key codes to readable names without WPF.
    /// </summary>
    internal static class VirtualKeyNames
    {
        internal static string GetName(int vk)
        {
            switch (vk)
            {
                case 0x08: return "Back";
                case 0x09: return "Tab";
                case 0x0D: return "Return";
                case 0x10: return "LeftShift";
                case 0x11: return "LeftCtrl";
                case 0x12: return "LeftAlt";
                case 0x13: return "Pause";
                case 0x14: return "CapsLock";
                case 0x1B: return "Escape";
                case 0x20: return "Space";
                case 0x21: return "PageUp";
                case 0x22: return "PageDown";
                case 0x23: return "End";
                case 0x24: return "Home";
                case 0x25: return "Left";
                case 0x26: return "Up";
                case 0x27: return "Right";
                case 0x28: return "Down";
                case 0x2D: return "Insert";
                case 0x2E: return "Delete";
                case 0x5B: return "LWin";
                case 0x5C: return "RWin";
                case 0xA0: return "LeftShift";
                case 0xA1: return "RightShift";
                case 0xA2: return "LeftCtrl";
                case 0xA3: return "RightCtrl";
                case 0xA4: return "LeftAlt";
                case 0xA5: return "RightAlt";
                default:
                    if (vk >= 0x30 && vk <= 0x39)
                    {
                        return ((char)vk).ToString();
                    }

                    if (vk >= 0x41 && vk <= 0x5A)
                    {
                        return ((char)vk).ToString();
                    }

                    if (vk >= 0x70 && vk <= 0x7B)
                    {
                        return "F" + (vk - 0x6F);
                    }

                    return "Key" + vk;
            }
        }
    }
}
