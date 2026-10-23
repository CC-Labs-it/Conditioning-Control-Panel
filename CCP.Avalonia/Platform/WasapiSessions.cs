using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// Every audio session on every active render endpoint, as WPF AudioService.CollectRenderSessions
    /// (NAudio) gathers them, through the raw CoreAudio COM interfaces so this head needs no NAudio.
    /// Only what ducking recovery uses: the owning PID and the session's own volume
    /// (ISimpleAudioVolume = the per-app slider in the Windows mixer, never the endpoint volume).
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class WasapiSessions : IDisposable
    {
        private readonly List<object> _com = new();

        internal List<DuckSession> Sessions { get; } = new();

        internal static WasapiSessions Collect()
        {
            var scope = new WasapiSessions();
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            scope._com.Add(enumerator);
            Check(enumerator.EnumAudioEndpoints(0 /* eRender */, 1 /* DEVICE_STATE_ACTIVE */, out var devices));
            scope._com.Add(devices);
            Check(devices.GetCount(out var count));
            var managerId = typeof(IAudioSessionManager2).GUID;
            for (uint d = 0; d < count; d++)
            {
                try
                {
                    Check(devices.Item(d, out var device));
                    scope._com.Add(device);
                    Check(device.Activate(ref managerId, 23 /* CLSCTX_ALL */, IntPtr.Zero, out var managerObj));
                    var manager = (IAudioSessionManager2)managerObj;
                    scope._com.Add(manager);
                    Check(manager.GetSessionEnumerator(out var sessions));
                    scope._com.Add(sessions);
                    Check(sessions.GetCount(out var n));
                    for (int i = 0; i < n; i++)
                    {
                        try
                        {
                            Check(sessions.GetSession(i, out var control));
                            scope._com.Add(control);
                            if (control.GetProcessId(out var pid) < 0) continue;
                            var volume = (ISimpleAudioVolume)control;
                            scope.Sessions.Add(new DuckSession((int)pid,
                                () => { Check(volume.GetMasterVolume(out var level)); return level; },
                                v =>
                                {
                                    var context = Guid.Empty;
                                    Check(volume.SetMasterVolume(Math.Clamp(v, 0f, 1f), ref context));
                                }));
                        }
                        catch { /* the session may have ended */ }
                    }
                }
                catch { /* an endpoint that went away mid-sweep */ }
            }
            return scope;
        }

        public void Dispose()
        {
            for (int i = _com.Count - 1; i >= 0; i--)
                try { Marshal.ReleaseComObject(_com[i]); } catch { }
            _com.Clear();
        }

        private static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumerator { }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
        private interface IMMDeviceCollection
        {
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int Item(uint index, out IMMDevice device);
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("D666063F-1587-4E43-81F1-B948E807363F")]
        private interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
        private interface IAudioSessionManager2
        {
            // IAudioSessionManager
            [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, int streamFlags, out IntPtr control);
            [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, int streamFlags, out IntPtr volume);
            // IAudioSessionManager2
            [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
        private interface IAudioSessionEnumerator
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int GetSession(int index, out IAudioSessionControl2 session);
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
        private interface IAudioSessionControl2
        {
            // IAudioSessionControl
            [PreserveSig] int GetState(out int state);
            [PreserveSig] int GetDisplayName(out IntPtr name);
            [PreserveSig] int SetDisplayName(IntPtr name, ref Guid context);
            [PreserveSig] int GetIconPath(out IntPtr path);
            [PreserveSig] int SetIconPath(IntPtr path, ref Guid context);
            [PreserveSig] int GetGroupingParam(out Guid param);
            [PreserveSig] int SetGroupingParam(ref Guid param, ref Guid context);
            [PreserveSig] int RegisterAudioSessionNotification(IntPtr client);
            [PreserveSig] int UnregisterAudioSessionNotification(IntPtr client);
            // IAudioSessionControl2
            [PreserveSig] int GetSessionIdentifier(out IntPtr id);
            [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
            [PreserveSig] int GetProcessId(out uint pid);
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
        private interface ISimpleAudioVolume
        {
            [PreserveSig] int SetMasterVolume(float level, ref Guid context);
            [PreserveSig] int GetMasterVolume(out float level);
        }
    }
}
