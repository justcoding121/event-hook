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
    /// Which <c>NSRunningApplication.activationPolicy</c> values ApplicationWatcher tracks.
    /// </summary>
    internal static class MacAppFilter
    {
        internal const int PolicyRegular = 0;
        internal const int PolicyAccessory = 1;
        internal const int PolicyProhibited = 2;

        internal static bool ShouldTrack(int activationPolicy) =>
            activationPolicy == PolicyRegular || activationPolicy == PolicyAccessory;
    }

    /// <summary>
    /// Launch / terminate / activate via <c>proc_listpids</c> + <c>NSRunningApplication</c> by PID.
    /// <c>NSWorkspace.runningApplications</c> stays stale in console / non-AppKit hosts (dotnet, CI, agents).
    /// </summary>
    internal sealed class MacApplication : IDisposable
    {
        private const int PidCapacity = 4096;

        private Action<MacAppSnapshot> onEvent;
        private Timer timer;
        private readonly Dictionary<int, (string Name, string Path)> known = new Dictionary<int, (string, string)>();
        private readonly int[] pidScratch = new int[PidCapacity];
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
                var cls = MacObjC.GetClass("NSRunningApplication");
                if (cls == IntPtr.Zero)
                {
                    return;
                }

                var written = MacNative.proc_listpids(MacNative.PROC_ALL_PIDS, 0, pidScratch, pidScratch.Length * sizeof(int));
                if (written <= 0)
                {
                    return;
                }

                var pidCount = written / sizeof(int);
                var seen = new HashSet<int>();
                var activePid = -1;
                var selByPid = MacObjC.Sel("runningApplicationWithProcessIdentifier:");
                var selPolicy = MacObjC.Sel("activationPolicy");
                var selName = MacObjC.Sel("localizedName");
                var selBundle = MacObjC.Sel("bundleURL");
                var selPath = MacObjC.Sel("path");
                var selActive = MacObjC.Sel("isActive");

                for (var i = 0; i < pidCount; i++)
                {
                    var pid = pidScratch[i];
                    if (pid <= 0)
                    {
                        continue;
                    }

                    var app = MacNative.objc_msgSend_int(cls, selByPid, pid);
                    if (app == IntPtr.Zero)
                    {
                        continue;
                    }

                    var policy = (int)MacNative.objc_msgSend_nint(app, selPolicy);
                    if (!MacAppFilter.ShouldTrack(policy))
                    {
                        continue;
                    }

                    seen.Add(pid);
                    var name = MacObjC.NSStringToString(MacNative.objc_msgSend(app, selName)) ?? string.Empty;
                    var url = MacNative.objc_msgSend(app, selBundle);
                    var path = url != IntPtr.Zero
                        ? MacObjC.NSStringToString(MacNative.objc_msgSend(url, selPath)) ?? string.Empty
                        : string.Empty;

                    if (MacNative.objc_msgSend_bool(app, selActive))
                    {
                        activePid = pid;
                    }

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

                    if (activePid > 0 && activePid != lastActivePid)
                    {
                        if (known.TryGetValue(activePid, out var info))
                        {
                            Emit(1, activePid, info.Name, info.Path);
                        }

                        lastActivePid = activePid;
                    }
                }
                else
                {
                    lastActivePid = activePid;
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
