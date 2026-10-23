using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// Windows ducking for <see cref="LibVlcAudio"/>: WPF AudioService's WASAPI sweep
    /// (ConditioningControlPanel/Services/AudioService.cs:979 ApplyDuckSweep, :1158 RestoreDuckedVolumes,
    /// :1373 CollectRenderSessions). Every session on every active render endpoint, keyed by process id;
    /// our own process and pid 0 (system sounds) are skipped; an original is captured once and never
    /// overwritten while it survives (WPF: recapturing is how volumes ratcheted toward 0%); a restore that
    /// throws keeps its original for the next one.
    /// shortcut: WPF's 2.5 s rescan, 3 min restore retry, process-name fallback, crash-recovery file and
    /// WebView2 exclusion are not ported (win-avalonia-parity-b); the Linux pactl sweep has none of them either.
    /// </summary>
    internal sealed class WasapiDuck
    {
        /// <summary>One live session: its process id and its ISimpleAudioVolume level.</summary>
        internal delegate void SessionVisitor(int pid, Func<float> get, Action<float> set);

        private readonly Action<SessionVisitor> _forEachSession;
        private readonly int _ownPid;
        private readonly Dictionary<int, float> _originals = new();

        /// <param name="forEachSession">Visits every render session (the WASAPI walk by default; tests hand in fakes).</param>
        internal WasapiDuck(Action<SessionVisitor>? forEachSession = null, int? ownPid = null)
        {
            _forEachSession = forEachSession ?? ForEachRenderSession;
            _ownPid = ownPid ?? Environment.ProcessId;
        }

        /// <summary>Scale every other app's session to <paramref name="keep"/>; stops early once unducked.</summary>
        internal void Sweep(double keep, Func<bool> stillDucked) => _forEachSession((pid, get, set) =>
        {
            if (pid == _ownPid || pid == 0 || !stillDucked()) return;
            var volume = get();
            lock (_originals) { if (!_originals.TryAdd(pid, volume)) return; }
            set(Math.Max(0f, volume * (float)keep));
        });

        /// <summary>Put every captured session back; an entry whose restore threw stays for the next one.</summary>
        internal void Restore()
        {
            Dictionary<int, float> wanted;
            lock (_originals) wanted = new(_originals);
            var failed = new HashSet<int>();
            _forEachSession((pid, _, set) =>
            {
                if (!wanted.TryGetValue(pid, out var original)) return;
                try { set(original); }
                catch { failed.Add(pid); }
            });
            lock (_originals)
                foreach (var pid in wanted.Keys)
                    if (!failed.Contains(pid)) _originals.Remove(pid);
        }

        // ---- WASAPI: render endpoints -> IAudioSessionManager2 -> sessions (pid + ISimpleAudioVolume) ----

        private const int ERender = 0;
        private const uint DeviceStateActive = 0x1, ClsCtxAll = 23;
        private static readonly Guid ClsidMmDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
        private static readonly Guid IidAudioSessionManager2 = new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, uint stateMask, out IMMDeviceCollection devices);
        }

        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceCollection
        {
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int Item(uint index, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        }

        [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionManager2
        {
            // IAudioSessionManager's two methods come first in the vtable; never called here.
            [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, uint streamFlags, out IntPtr control);
            [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, uint streamFlags, out IntPtr volume);
            [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
        }

        [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionEnumerator
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object session);
        }

        [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionControl2
        {
            // IAudioSessionControl's nine methods, then IAudioSessionControl2's; only GetProcessId is called.
            [PreserveSig] int GetState(out int state);
            [PreserveSig] int GetDisplayName(out IntPtr name);
            [PreserveSig] int SetDisplayName(IntPtr name, IntPtr context);
            [PreserveSig] int GetIconPath(out IntPtr path);
            [PreserveSig] int SetIconPath(IntPtr path, IntPtr context);
            [PreserveSig] int GetGroupingParam(out Guid param);
            [PreserveSig] int SetGroupingParam(IntPtr param, IntPtr context);
            [PreserveSig] int RegisterAudioSessionNotification(IntPtr client);
            [PreserveSig] int UnregisterAudioSessionNotification(IntPtr client);
            [PreserveSig] int GetSessionIdentifier(out IntPtr id);
            [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
            [PreserveSig] int GetProcessId(out uint pid);
        }

        [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISimpleAudioVolume
        {
            [PreserveSig] int SetMasterVolume(float level, IntPtr eventContext);
            [PreserveSig] int GetMasterVolume(out float level);
        }

        private static void ForEachRenderSession(SessionVisitor visit)
        {
            if (!OperatingSystem.IsWindows()) return;
            // Every wrapper is released on the way out (WPF #686: one left to the finalizer leaks a native handle).
            var owned = new List<object?>();
            try
            {
                var type = Type.GetTypeFromCLSID(ClsidMmDeviceEnumerator);
                if (type == null || Activator.CreateInstance(type) is not IMMDeviceEnumerator enumerator) return;
                owned.Add(enumerator);
                if (enumerator.EnumAudioEndpoints(ERender, DeviceStateActive, out var endpoints) != 0 || endpoints == null) return;
                owned.Add(endpoints);
                if (endpoints.GetCount(out var deviceCount) != 0) return;

                for (uint d = 0; d < deviceCount; d++)
                {
                    try
                    {
                        if (endpoints.Item(d, out var device) != 0 || device == null) continue;
                        owned.Add(device);
                        var iid = IidAudioSessionManager2;
                        if (device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out var activated) != 0) continue;
                        owned.Add(activated);
                        if (activated is not IAudioSessionManager2 manager
                            || manager.GetSessionEnumerator(out var sessions) != 0 || sessions == null) continue;
                        owned.Add(sessions);
                        if (sessions.GetCount(out var count) != 0) continue;

                        for (var i = 0; i < count; i++)
                        {
                            try
                            {
                                if (sessions.GetSession(i, out var session) != 0 || session == null) continue;
                                owned.Add(session);
                                if (session is not IAudioSessionControl2 control || control.GetProcessId(out var pid) != 0
                                    || session is not ISimpleAudioVolume volume) continue;
                                visit((int)pid,
                                    () => volume.GetMasterVolume(out var level) == 0 ? level : throw new InvalidOperationException("GetMasterVolume failed"),
                                    level => Marshal.ThrowExceptionForHR(volume.SetMasterVolume(level, IntPtr.Zero)));
                            }
                            catch (Exception ex) { Log.Debug("[Ducking] audio session skipped: {Error}", ex.Message); } // ended mid-sweep
                        }
                    }
                    catch (Exception ex) { Log.Debug("[Ducking] render endpoint skipped: {Error}", ex.Message); }
                }
            }
            finally
            {
                for (var i = owned.Count - 1; i >= 0; i--)
                {
                    try { if (owned[i] is { } com && Marshal.IsComObject(com)) Marshal.ReleaseComObject(com); } catch { }
                }
            }
        }
    }
}
