using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using EventHook.Helpers;
using EventHook.Platforms.Mac.Native;

namespace EventHook.Platforms.Mac
{
    /// <summary>
    /// Clipboard change notification: changeCount only. Payload is read on the consumer.
    /// </summary>
    internal readonly struct MacClipboardSnapshot
    {
        internal MacClipboardSnapshot(nint changeCount)
        {
            ChangeCount = changeCount;
        }

        internal nint ChangeCount { get; }
    }

    /// <summary>
    /// Polls NSPasteboard.generalPasteboard.changeCount (~200ms).
    /// </summary>
    internal sealed class MacClipboard : IDisposable
    {
        private Action<MacClipboardSnapshot> onChange;
        private Timer timer;
        private nint lastCount = -1;
        private bool disposed;

        internal HookStartResult Start(Action<MacClipboardSnapshot> enqueue)
        {
            if (enqueue == null)
            {
                throw new ArgumentNullException(nameof(enqueue));
            }

            if (timer != null)
            {
                return HookStartResult.Ok();
            }

            try
            {
                MacNative.NSApplicationLoad();
            }
            catch
            {
                // ignore
            }

            onChange = enqueue;
            lastCount = ReadChangeCount();
            timer = new Timer(OnPoll, null, TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(200));
            return HookStartResult.Ok();
        }

        internal void Stop()
        {
            timer?.Dispose();
            timer = null;
            onChange = null;
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

        /// <summary>
        /// Read current pasteboard payload on the consumer thread.
        /// </summary>
        internal static bool TryRead(out ClipboardContentTypes format, out object data)
        {
            format = ClipboardContentTypes.Other;
            data = null;

            try
            {
                var pasteboard = GeneralPasteboard();
                if (pasteboard == IntPtr.Zero)
                {
                    return false;
                }

                var typeName = MacObjC.CreateNSString("public.utf8-plain-text");
                var value = MacObjC.MsgSend(pasteboard, MacObjC.Sel("stringForType:"), typeName);
                if (value == IntPtr.Zero)
                {
                    typeName = MacObjC.CreateNSString("NSStringPboardType");
                    value = MacObjC.MsgSend(pasteboard, MacObjC.Sel("stringForType:"), typeName);
                }

                if (value == IntPtr.Zero)
                {
                    return false;
                }

                var utf8 = MacNative.objc_msgSend(value, MacObjC.Sel("UTF8String"));
                if (utf8 == IntPtr.Zero)
                {
                    return false;
                }

                data = Marshal.PtrToStringUTF8(utf8);
                format = ClipboardContentTypes.UnicodeText;
                return data != null;
            }
            catch
            {
                return false;
            }
        }

        private void OnPoll(object state)
        {
            try
            {
                var count = ReadChangeCount();
                if (count < 0 || count == lastCount)
                {
                    return;
                }

                lastCount = count;
                onChange?.Invoke(new MacClipboardSnapshot(count));
            }
            catch
            {
                // never throw from timer
            }
        }

        private static nint ReadChangeCount()
        {
            var pb = GeneralPasteboard();
            if (pb == IntPtr.Zero)
            {
                return -1;
            }

            return MacNative.objc_msgSend_nint(pb, MacObjC.Sel("changeCount"));
        }

        private static IntPtr GeneralPasteboard()
        {
            var cls = MacObjC.GetClass("NSPasteboard");
            if (cls == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            return MacNative.objc_msgSend(cls, MacObjC.Sel("generalPasteboard"));
        }
    }

    /// <summary>
    /// Tiny objc helpers shared by Mac clipboard / application backends.
    /// </summary>
    internal static class MacObjC
    {
        internal static IntPtr Sel(string name) => MacNative.sel_registerName(name);

        internal static IntPtr GetClass(string name) => MacNative.objc_getClass(name);

        internal static IntPtr MsgSend(IntPtr receiver, IntPtr sel) => MacNative.objc_msgSend(receiver, sel);

        internal static IntPtr MsgSend(IntPtr receiver, IntPtr sel, IntPtr arg1) =>
            MacNative.objc_msgSend_IntPtr(receiver, sel, arg1);

        internal static IntPtr CreateNSString(string value)
        {
            var cls = GetClass("NSString");
            var utf8 = Marshal.StringToCoTaskMemUTF8(value);
            try
            {
                return MacNative.objc_msgSend_IntPtr(cls, Sel("stringWithUTF8String:"), utf8);
            }
            finally
            {
                Marshal.FreeCoTaskMem(utf8);
            }
        }

        internal static string NSStringToString(IntPtr nsString)
        {
            if (nsString == IntPtr.Zero)
            {
                return null;
            }

            var utf8 = MacNative.objc_msgSend(nsString, Sel("UTF8String"));
            return utf8 == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(utf8);
        }
    }
}
