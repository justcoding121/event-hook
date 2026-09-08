using System;
using System.Runtime.InteropServices;

namespace EventHook.Platforms.Linux
{
    internal static class LinuxX11Native
    {
        internal const int KeyPress = 2;
        internal const int KeyRelease = 3;
        internal const int ButtonPress = 4;
        internal const int ButtonRelease = 5;
        internal const int MotionNotify = 6;
        internal const int CreateNotify = 16;
        internal const int DestroyNotify = 17;
        internal const int PropertyNotify = 28;
        internal const int ClientMessage = 33;

        internal const int BadAccess = 10;
        internal const int GrabModeAsync = 1;
        internal const int AnyModifier = 1 << 15;
        internal const int None = 0;
        internal const int CurrentTime = 0;
        internal const int PropModeReplace = 0;

        internal const long PropertyChangeMask = 1L << 22;
        internal const long SubstructureNotifyMask = 1L << 19;
        internal const long StructureNotifyMask = 1L << 17;
        internal const long KeyPressMask = 1L << 0;

        internal const int XRecordFromServer = 0;
        internal const int XRecordFromClient = 1;
        internal const int XRecordClientStarted = 2;
        internal const int XRecordClientDied = 3;
        internal const int XRecordStartOfData = 4;
        internal const int XRecordEndOfData = 5;

        internal const int KeyPressDetail = 2;
        internal const int KeyReleaseDetail = 3;
        internal const int ButtonPressDetail = 4;
        internal const int ButtonReleaseDetail = 5;
        internal const int MotionNotifyDetail = 6;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int XErrorHandler(IntPtr display, ref XErrorEvent errorEvent);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void XRecordInterceptProc(IntPtr closure, IntPtr recordedData);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int XInitThreads();

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int XSynchronize(IntPtr display, int onoff);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr XOpenDisplay(string displayName);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int XCloseDisplay(IntPtr display);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int XDefaultScreen(IntPtr display);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr XRootWindow(IntPtr display, int screen);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int XConnectionNumber(IntPtr display);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int XPending(IntPtr display);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int XNextEvent(IntPtr display, out XEvent xevent);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int XFlush(IntPtr display);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int XSync(IntPtr display, int discard);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        internal static extern XErrorHandler XSetErrorHandler(XErrorHandler handler);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong XKeycodeToKeysym(IntPtr display, uint keycode, int index);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int XDisplayKeycodes(IntPtr display, out int minKeycodes, out int maxKeycodes);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint XKeysymToKeycode(IntPtr display, ulong keysym);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int XGrabKey(
            IntPtr display,
            int keycode,
            uint modifiers,
            IntPtr grabWindow,
            int ownerEvents,
            int pointerMode,
            int keyboardMode);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int XUngrabKey(IntPtr display, int keycode, uint modifiers, IntPtr grabWindow);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int XSelectInput(IntPtr display, IntPtr window, long eventMask);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr XInternAtom(IntPtr display, string atomName, int onlyIfExists);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int XGetWindowProperty(
            IntPtr display,
            IntPtr window,
            IntPtr property,
            long offset,
            long length,
            int delete,
            IntPtr reqType,
            out IntPtr actualType,
            out int actualFormat,
            out ulong nItems,
            out ulong bytesAfter,
            out IntPtr prop);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int XFree(IntPtr data);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr XCreateSimpleWindow(
            IntPtr display,
            IntPtr parent,
            int x,
            int y,
            uint width,
            uint height,
            uint borderWidth,
            ulong border,
            ulong background);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int XDestroyWindow(IntPtr display, IntPtr window);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int XMapWindow(IntPtr display, IntPtr window);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int XQueryTree(
            IntPtr display,
            IntPtr window,
            out IntPtr root,
            out IntPtr parent,
            out IntPtr children,
            out uint nChildren);

        [DllImport("libXtst.so.6", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int XRecordQueryVersion(IntPtr display, out int majorVersion, out int minorVersion);

        [DllImport("libXtst.so.6", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr XRecordAllocRange();

        [DllImport("libXtst.so.6", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr XRecordCreateContext(
            IntPtr display,
            int datumFlags,
            IntPtr[] clients,
            int nClients,
            IntPtr[] ranges,
            int nRanges);

        [DllImport("libXtst.so.6", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int XRecordEnableContext(
            IntPtr display,
            IntPtr context,
            XRecordInterceptProc callback,
            IntPtr closure);

        [DllImport("libXtst.so.6", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int XRecordDisableContext(IntPtr display, IntPtr context);

        [DllImport("libXtst.so.6", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int XRecordFreeContext(IntPtr display, IntPtr context);

        [DllImport("libXtst.so.6", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void XRecordFreeData(IntPtr data);

        [DllImport("libXfixes.so.3", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr XFixesQueryExtension(IntPtr display, out int eventBase, out int errorBase);

        [DllImport("libXfixes.so.3", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void XFixesSelectSelectionInput(
            IntPtr display,
            IntPtr window,
            IntPtr selection,
            ulong eventMask);

        internal const ulong XFixesSetSelectionOwnerNotifyMask = 1UL << 0;
        internal const ulong XFixesSelectionWindowDestroyNotifyMask = 1UL << 1;
        internal const ulong XFixesSelectionClientCloseNotifyMask = 1UL << 2;

        // Modifier masks
        internal const uint ShiftMask = 1 << 0;
        internal const uint LockMask = 1 << 1;
        internal const uint ControlMask = 1 << 2;
        internal const uint Mod1Mask = 1 << 3; // Alt
        internal const uint Mod2Mask = 1 << 4;
        internal const uint Mod3Mask = 1 << 5;
        internal const uint Mod4Mask = 1 << 6; // Super
        internal const uint Mod5Mask = 1 << 7;

        [StructLayout(LayoutKind.Sequential)]
        internal struct XErrorEvent
        {
            public int type;
            public IntPtr display;
            public ulong resourceid;
            public ulong serial;
            public byte error_code;
            public byte request_code;
            public byte minor_code;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct XRecordRange8
        {
            public byte first;
            public byte last;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct XRecordRange16
        {
            public ushort first;
            public ushort last;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct XRecordExtRange
        {
            public XRecordRange8 major;
            public XRecordRange16 minor;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct XRecordRange
        {
            public XRecordRange8 coreRequests;
            public XRecordRange8 coreReplies;
            public XRecordExtRange extRequests;
            public XRecordExtRange extReplies;
            public XRecordRange8 deliveredEvents;
            public XRecordRange8 deviceEvents;
            public XRecordRange8 errors;
            public int clientStarted;
            public int clientDied;
        }

        /// <summary>
        /// Native <c>XRecordInterceptData</c> on LP64: XID/Time/unsigned long are 8 bytes.
        /// <c>data_len</c> is in 4-byte units.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct XRecordInterceptData
        {
            public ulong idBase;
            public ulong serverTime;
            public ulong clientSequence;
            public int category;
            public int clientSwapped;
            public IntPtr data;
            public ulong dataLen;
        }

        /// <summary>
        /// Core device-event wire format delivered by XRecord (<c>xEvent</c>, 32 bytes) — not Xlib <c>XEvent</c>.
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct XRecordWireEvent
        {
            public byte type;
            public byte detail;
            public ushort sequenceNumber;
            public uint time;
            public uint root;
            public uint eventWindow;
            public uint child;
            public short rootX;
            public short rootY;
            public short eventX;
            public short eventY;
            public ushort state;
            public byte sameScreen;
            public byte pad;
        }

        // XEvent is a large union; keep a pad large enough for 64-bit Xlib.
        [StructLayout(LayoutKind.Sequential)]
        internal struct XEvent
        {
            public int type;
            public IntPtr pad0;
            public IntPtr pad1;
            public IntPtr pad2;
            public IntPtr pad3;
            public IntPtr pad4;
            public IntPtr pad5;
            public IntPtr pad6;
            public IntPtr pad7;
            public IntPtr pad8;
            public IntPtr pad9;
            public IntPtr pad10;
            public IntPtr pad11;
            public IntPtr pad12;
            public IntPtr pad13;
            public IntPtr pad14;
            public IntPtr pad15;
            public IntPtr pad16;
            public IntPtr pad17;
            public IntPtr pad18;
            public IntPtr pad19;
            public IntPtr pad20;
            public IntPtr pad21;
            public IntPtr pad22;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct XKeyEvent
        {
            public int type;
            public ulong serial;
            public int send_event;
            public IntPtr display;
            public IntPtr window;
            public IntPtr root;
            public IntPtr subwindow;
            public ulong time;
            public int x;
            public int y;
            public int x_root;
            public int y_root;
            public uint state;
            public uint keycode;
            public int same_screen;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct XButtonEvent
        {
            public int type;
            public ulong serial;
            public int send_event;
            public IntPtr display;
            public IntPtr window;
            public IntPtr root;
            public IntPtr subwindow;
            public ulong time;
            public int x;
            public int y;
            public int x_root;
            public int y_root;
            public uint state;
            public uint button;
            public int same_screen;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct XMotionEvent
        {
            public int type;
            public ulong serial;
            public int send_event;
            public IntPtr display;
            public IntPtr window;
            public IntPtr root;
            public IntPtr subwindow;
            public ulong time;
            public int x;
            public int y;
            public int x_root;
            public int y_root;
            public uint state;
            public byte is_hint;
            public int same_screen;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct XCreateWindowEvent
        {
            public int type;
            public ulong serial;
            public int send_event;
            public IntPtr display;
            public IntPtr parent;
            public IntPtr window;
            public int x;
            public int y;
            public int width;
            public int height;
            public int border_width;
            public int override_redirect;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct XDestroyWindowEvent
        {
            public int type;
            public ulong serial;
            public int send_event;
            public IntPtr display;
            public IntPtr eventWindow;
            public IntPtr window;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct XPropertyEvent
        {
            public int type;
            public ulong serial;
            public int send_event;
            public IntPtr display;
            public IntPtr window;
            public IntPtr atom;
            public ulong time;
            public int state;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct XFixesSelectionNotifyEvent
        {
            public int type;
            public ulong serial;
            public int send_event;
            public IntPtr display;
            public IntPtr window;
            public int subtype;
            public IntPtr owner;
            public IntPtr selection;
            public ulong timestamp;
            public ulong selectionTimestamp;
        }

        internal static T PtrToStructure<T>(IntPtr ptr) where T : struct =>
            Marshal.PtrToStructure<T>(ptr);

        internal static T EventAs<T>(ref XEvent ev) where T : struct
        {
            var handle = GCHandle.Alloc(ev, GCHandleType.Pinned);
            try
            {
                return Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject());
            }
            finally
            {
                handle.Free();
            }
        }
    }
}
