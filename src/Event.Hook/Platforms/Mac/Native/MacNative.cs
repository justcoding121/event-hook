using System;
using System.Runtime.InteropServices;

namespace EventHook.Platforms.Mac.Native
{
    /// <summary>
    /// P/Invoke surface for CoreFoundation, CoreGraphics, Carbon, AppKit/objc, Accessibility, and CUPS.
    /// Safe to compile on any OS; call only when <see cref="OperatingSystem.IsMacOS"/> is true.
    /// Do not use static field initializers that invoke native APIs (would fault on non-macOS type load).
    /// </summary>
    internal static class MacNative
    {
        internal const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        internal const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
        internal const string ApplicationServices = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
        internal const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";
        internal const string AppKit = "/System/Library/Frameworks/AppKit.framework/AppKit";
        internal const string ObjC = "/usr/lib/libobjc.dylib";
        internal const string Cups = "cups";

        private static IntPtr cfRunLoopDefaultMode;
        private static IntPtr cfRunLoopCommonModes;

        internal static IntPtr KCFRunLoopDefaultMode
        {
            get
            {
                if (cfRunLoopDefaultMode == IntPtr.Zero)
                {
                    cfRunLoopDefaultMode = CFStringCreateWithCString(IntPtr.Zero, "kCFRunLoopDefaultMode", 0x08000100);
                }

                return cfRunLoopDefaultMode;
            }
        }

        internal static IntPtr KCFRunLoopCommonModes
        {
            get
            {
                if (cfRunLoopCommonModes == IntPtr.Zero)
                {
                    cfRunLoopCommonModes = CFStringCreateWithCString(IntPtr.Zero, "kCFRunLoopCommonModes", 0x08000100);
                }

                return cfRunLoopCommonModes;
            }
        }

        // --- CoreFoundation ---

        [DllImport(CoreFoundation)]
        internal static extern IntPtr CFRunLoopGetCurrent();

        [DllImport(CoreFoundation)]
        internal static extern void CFRunLoopRun();

        [DllImport(CoreFoundation)]
        internal static extern void CFRunLoopStop(IntPtr rl);

        [DllImport(CoreFoundation)]
        internal static extern void CFRunLoopWakeUp(IntPtr rl);

        [DllImport(CoreFoundation)]
        internal static extern void CFRunLoopAddSource(IntPtr rl, IntPtr source, IntPtr mode);

        [DllImport(CoreFoundation)]
        internal static extern void CFRunLoopRemoveSource(IntPtr rl, IntPtr source, IntPtr mode);

        [DllImport(CoreFoundation)]
        internal static extern IntPtr CFRunLoopSourceCreate(IntPtr allocator, nint order, ref CFRunLoopSourceContext context);

        [DllImport(CoreFoundation)]
        internal static extern void CFRunLoopSourceSignal(IntPtr source);

        [DllImport(CoreFoundation)]
        internal static extern void CFRelease(IntPtr cf);

        [DllImport(CoreFoundation)]
        internal static extern IntPtr CFRetain(IntPtr cf);

        [DllImport(CoreFoundation)]
        internal static extern IntPtr CFStringCreateWithCString(IntPtr alloc, string cStr, uint encoding);

        [DllImport(CoreFoundation)]
        internal static extern IntPtr CFMachPortCreateRunLoopSource(IntPtr allocator, IntPtr port, nint order);

        [DllImport(CoreFoundation)]
        internal static extern bool CFStringGetCString(IntPtr theString, byte[] buffer, nint bufferSize, uint encoding);

        [DllImport(CoreFoundation)]
        internal static extern IntPtr CFArrayGetCount(IntPtr theArray);

        [DllImport(CoreFoundation)]
        internal static extern IntPtr CFArrayGetValueAtIndex(IntPtr theArray, nint idx);

        [StructLayout(LayoutKind.Sequential)]
        internal struct CFRunLoopSourceContext
        {
            public nint version;
            public IntPtr info;
            public IntPtr retain;
            public IntPtr release;
            public IntPtr copyDescription;
            public IntPtr equal;
            public IntPtr hash;
            public IntPtr schedule;
            public IntPtr cancel;
            public IntPtr perform;
        }

        internal delegate void CFRunLoopSourcePerform(IntPtr info);

        // --- CoreGraphics event tap ---

        internal const uint kCGSessionEventTap = 1;
        internal const uint kCGHeadInsertEventTap = 0;
        internal const uint kCGEventTapOptionListenOnly = 1;

        internal const uint kCGEventLeftMouseDown = 1;
        internal const uint kCGEventLeftMouseUp = 2;
        internal const uint kCGEventRightMouseDown = 3;
        internal const uint kCGEventRightMouseUp = 4;
        internal const uint kCGEventMouseMoved = 5;
        internal const uint kCGEventLeftMouseDragged = 6;
        internal const uint kCGEventRightMouseDragged = 7;
        internal const uint kCGEventKeyDown = 10;
        internal const uint kCGEventKeyUp = 11;
        internal const uint kCGEventFlagsChanged = 12;
        internal const uint kCGEventScrollWheel = 22;
        internal const uint kCGEventOtherMouseDown = 25;
        internal const uint kCGEventOtherMouseUp = 26;
        internal const uint kCGEventOtherMouseDragged = 27;

        internal const uint kCGMouseEventButtonNumber = 3;
        internal const uint kCGScrollWheelEventDeltaAxis1 = 11;
        internal const uint kCGKeyboardEventKeycode = 9;

        [DllImport(CoreGraphics)]
        internal static extern IntPtr CGEventTapCreate(
            uint tap,
            uint place,
            uint options,
            ulong eventsOfInterest,
            CGEventTapCallBack callback,
            IntPtr userInfo);

        [DllImport(CoreGraphics)]
        internal static extern void CGEventTapEnable(IntPtr tap, bool enable);

        [DllImport(CoreGraphics)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool CGEventTapIsEnabled(IntPtr tap);

        [DllImport(CoreGraphics)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool CGPreflightListenEventAccess();

        [DllImport(CoreGraphics)]
        internal static extern IntPtr CGEventGetIntegerValueField(IntPtr cgEvent, uint field);

        [DllImport(CoreGraphics)]
        internal static extern double CGEventGetDoubleValueField(IntPtr cgEvent, uint field);

        [DllImport(CoreGraphics)]
        internal static extern CGPoint CGEventGetLocation(IntPtr cgEvent);

        [DllImport(CoreGraphics)]
        internal static extern ulong CGEventGetFlags(IntPtr cgEvent);

        [DllImport(CoreGraphics)]
        internal static extern void CGEventKeyboardGetUnicodeString(
            IntPtr cgEvent,
            nuint maxLength,
            out nuint actualLength,
            [Out] ushort[] unicodeString);

        [DllImport(CoreGraphics)]
        internal static extern uint CGEventGetType(IntPtr cgEvent);

        internal delegate IntPtr CGEventTapCallBack(IntPtr proxy, uint type, IntPtr eventRef, IntPtr userInfo);

        [StructLayout(LayoutKind.Sequential)]
        internal struct CGPoint
        {
            public double X;
            public double Y;
        }

        internal static ulong CGEventMaskBit(uint eventType) => 1UL << (int)eventType;

        // --- Accessibility ---

        [DllImport(ApplicationServices)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool AXIsProcessTrusted();

        [DllImport(ApplicationServices)]
        internal static extern IntPtr AXUIElementCreateSystemWide();

        [DllImport(ApplicationServices)]
        internal static extern IntPtr AXUIElementCreateApplication(int pid);

        [DllImport(ApplicationServices)]
        internal static extern int AXObserverCreate(int pid, AXObserverCallback callback, out IntPtr observer);

        [DllImport(ApplicationServices)]
        internal static extern int AXObserverAddNotification(IntPtr observer, IntPtr element, IntPtr notification, IntPtr refcon);

        [DllImport(ApplicationServices)]
        internal static extern IntPtr AXObserverGetRunLoopSource(IntPtr observer);

        [DllImport(ApplicationServices)]
        internal static extern int AXUIElementCopyAttributeValue(IntPtr element, IntPtr attribute, out IntPtr value);

        internal delegate void AXObserverCallback(IntPtr observer, IntPtr element, IntPtr notification, IntPtr refcon);

        // --- Carbon hotkeys ---

        internal const uint cmdKey = 1 << 8;
        internal const uint shiftKey = 1 << 9;
        internal const uint optionKey = 1 << 11;
        internal const uint controlKey = 1 << 12;

        internal const uint kEventClassKeyboard = 0x6B657962; // 'keyb'
        internal const uint kEventHotKeyPressed = 5;
        internal const uint kEventParamDirectObject = 0x2D2D2D2D; // '----'
        internal const uint typeEventHotKeyID = 0x686B6964; // 'hkid'

        [StructLayout(LayoutKind.Sequential)]
        internal struct EventHotKeyID
        {
            public uint signature;
            public uint id;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct EventTypeSpec
        {
            public uint eventClass;
            public uint eventKind;
        }

        [DllImport(Carbon)]
        internal static extern int RegisterEventHotKey(
            uint inHotKeyCode,
            uint inHotKeyModifiers,
            EventHotKeyID inHotKeyID,
            IntPtr inTarget,
            uint inOptions,
            out IntPtr outRef);

        [DllImport(Carbon)]
        internal static extern int UnregisterEventHotKey(IntPtr inHotKey);

        [DllImport(Carbon)]
        internal static extern IntPtr GetEventDispatcherTarget();

        [DllImport(Carbon)]
        internal static extern int InstallEventHandler(
            IntPtr target,
            EventHandlerProc handlerProc,
            nint numTypes,
            EventTypeSpec[] typeList,
            IntPtr userData,
            out IntPtr handlerRef);

        [DllImport(Carbon)]
        internal static extern int RemoveEventHandler(IntPtr handlerRef);

        [DllImport(Carbon)]
        internal static extern int GetEventParameter(
            IntPtr inEvent,
            uint inName,
            uint inDesiredType,
            IntPtr outActualType,
            nint inBufferSize,
            IntPtr outActualSize,
            out EventHotKeyID outData);

        internal delegate int EventHandlerProc(IntPtr nextHandler, IntPtr theEvent, IntPtr userData);

        // --- objc / AppKit ---

        [DllImport(ObjC, EntryPoint = "objc_getClass")]
        internal static extern IntPtr objc_getClass(string name);

        [DllImport(ObjC, EntryPoint = "sel_registerName")]
        internal static extern IntPtr sel_registerName(string name);

        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        internal static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        internal static extern IntPtr objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);

        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        internal static extern IntPtr objc_msgSend_IntPtr_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2);

        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        internal static extern nint objc_msgSend_nint(IntPtr receiver, IntPtr selector);

        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        internal static extern void objc_msgSend_void_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);

        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        internal static extern void objc_msgSend_void_IntPtr_IntPtr_IntPtr(
            IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2, IntPtr arg3);

        [DllImport(AppKit)]
        internal static extern void NSApplicationLoad();

        // --- CUPS ---

        [StructLayout(LayoutKind.Sequential)]
        internal struct cups_dest_t
        {
            public IntPtr name;
            public IntPtr instance;
            public int is_default;
            public int num_options;
            public IntPtr options;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct cups_job_t
        {
            public int id;
            public IntPtr dest;
            public IntPtr title;
            public IntPtr user;
            public IntPtr format;
            public int state;
            public int size;
            public int priority;
            public long completed_time;
            public long creation_time;
            public long processing_time;
        }

        [DllImport(Cups)]
        internal static extern int cupsGetDests(out IntPtr dests);

        [DllImport(Cups)]
        internal static extern void cupsFreeDests(int num_dests, IntPtr dests);

        [DllImport(Cups)]
        internal static extern int cupsGetJobs(out IntPtr jobs, string name, int myjobs, int whichjobs);

        [DllImport(Cups)]
        internal static extern void cupsFreeJobs(int num_jobs, IntPtr jobs);

        internal const int CUPS_WHICHJOBS_ACTIVE = 0;
    }
}
