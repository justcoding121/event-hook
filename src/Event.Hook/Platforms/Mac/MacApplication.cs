using System;
using System.Collections.Generic;
using System.Threading;
using EventHook.Helpers;
using EventHook.Platforms.Mac.Native;

namespace EventHook.Platforms.Mac
{
    /// <summary>
    /// Application event snapshot from NSWorkspace polling (POD copy for TryWrite).
    /// </summary>
    internal struct MacAppSnapshot
    {
        internal int EventKind; // 0 launched, 1 activated, 2 closed
        internal int Pid;
        private readonly byte[] nameUtf8;
        private readonly byte[] pathUtf8;
        private int nameLen;
        private int pathLen;

        internal MacAppSnapshot(int eventKind, int pid)
        {
            EventKind = eventKind;
            Pid = pid;
            nameUtf8 = new byte[256];
            pathUtf8 = new byte[512];
            nameLen = 0;
            pathLen = 0;
        }

        internal void SetName(string value) => Write(value, ref nameLen, nameUtf8);

        internal void SetPath(string value) => Write(value, ref pathLen, pathUtf8);

        internal string GetName() => Read(nameLen, nameUtf8);

        internal string GetPath() => Read(pathLen, pathUtf8);

        private static void Write(string value, ref int length, byte[] dest)
        {
            if (string.IsNullOrEmpty(value))
            {
                length = 0;
                return;
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(value);
            var copy = bytes.Length < dest.Length - 1 ? bytes.Length : dest.Length - 1;
            Buffer.BlockCopy(bytes, 0, dest, 0, copy);
            dest[copy] = 0;
            length = copy;
        }

        private static string Read(int length, byte[] src)
        {
            if (length <= 0)
            {
                return string.Empty;
            }

            return System.Text.Encoding.UTF8.GetString(src, 0, length);
        }
    }

    /// <summary>
    /// NSWorkspace launch / terminate / activate via runningApplications polling (no Accessibility).
    /// Notification-center selectors need an ObjC subclass; polling keeps the portable P/Invoke surface reliable.
    /// </summary>
    internal sealed class MacApplication : IDisposable
    {
        private Action<MacAppSnapshot> onEvent;
        private Timer timer;
        private readonly Dictionary<int, (string Name, string Path)> known = new Dictionary<int, (string, string)>();
        private int lastActivePid = -1;
        private bool disposed;
        private bool primed;

        internal HookStartResult Start(Action<MacAppSnapshot> enqueue)
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

            onEvent = enqueue;
            timer = new Timer(OnPoll, null, TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(400));
            return HookStartResult.Ok();
        }

        internal void Stop()
        {
            timer?.Dispose();
            timer = null;
            onEvent = null;
            known.Clear();
            primed = false;
            lastActivePid = -1;
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

        private void OnPoll(object state)
        {
            try
            {
                var workspace = MacNative.objc_msgSend(MacObjC.GetClass("NSWorkspace"), MacObjC.Sel("sharedWorkspace"));
                if (workspace == IntPtr.Zero)
                {
                    return;
                }

                var apps = MacNative.objc_msgSend(workspace, MacObjC.Sel("runningApplications"));
                if (apps == IntPtr.Zero)
                {
                    return;
                }

                var count = (int)MacNative.objc_msgSend_nint(apps, MacObjC.Sel("count"));
                var seen = new HashSet<int>();
                var front = MacNative.objc_msgSend(workspace, MacObjC.Sel("frontmostApplication"));
                var frontPid = front != IntPtr.Zero
                    ? (int)MacNative.objc_msgSend_nint(front, MacObjC.Sel("processIdentifier"))
                    : -1;

                for (var i = 0; i < count; i++)
                {
                    var app = MacNative.objc_msgSend_IntPtr(apps, MacObjC.Sel("objectAtIndex:"), new IntPtr(i));
                    if (app == IntPtr.Zero)
                    {
                        continue;
                    }

                    var pid = (int)MacNative.objc_msgSend_nint(app, MacObjC.Sel("processIdentifier"));
                    seen.Add(pid);
                    var name = MacObjC.NSStringToString(MacNative.objc_msgSend(app, MacObjC.Sel("localizedName"))) ?? string.Empty;
                    var url = MacNative.objc_msgSend(app, MacObjC.Sel("bundleURL"));
                    var path = url != IntPtr.Zero
                        ? MacObjC.NSStringToString(MacNative.objc_msgSend(url, MacObjC.Sel("path"))) ?? string.Empty
                        : string.Empty;

                    if (!known.ContainsKey(pid))
                    {
                        known[pid] = (name, path);
                        if (primed)
                        {
                            Emit(0, pid, name, path);
                        }
                    }
                }

                if (primed)
                {
                    var toRemove = new List<int>();
                    foreach (var kv in known)
                    {
                        if (!seen.Contains(kv.Key))
                        {
                            toRemove.Add(kv.Key);
                        }
                    }

                    foreach (var pid in toRemove)
                    {
                        var info = known[pid];
                        known.Remove(pid);
                        Emit(2, pid, info.Name, info.Path);
                    }

                    if (frontPid > 0 && frontPid != lastActivePid)
                    {
                        if (known.TryGetValue(frontPid, out var info))
                        {
                            Emit(1, frontPid, info.Name, info.Path);
                        }

                        lastActivePid = frontPid;
                    }
                }
                else
                {
                    lastActivePid = frontPid;
                    primed = true;
                }
            }
            catch
            {
                // never throw from timer
            }
        }

        private void Emit(int kind, int pid, string name, string path)
        {
            var snap = new MacAppSnapshot(kind, pid);
            snap.SetName(name);
            snap.SetPath(path);
            onEvent?.Invoke(snap);
        }
    }
}
