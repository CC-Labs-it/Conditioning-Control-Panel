using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using ConditioningControlPanel.Services.Awareness;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace ConditioningControlPanel.Avalonia.Platform;

// WPF AwarenessProbes.cs:328: cache the capture-session sweep for five seconds.
internal sealed class Win32AwarenessMicrophoneProbe : IMicrophoneProbe
{
    private readonly object _gate = new();
    private readonly Func<bool> _sweep;
    private DateTime _checkedAt = DateTime.MinValue;
    private bool _inUse;

    internal Win32AwarenessMicrophoneProbe() : this(() => OperatingSystem.IsWindows() && Sweep()) { }
    internal Win32AwarenessMicrophoneProbe(Func<bool> sweep) => _sweep = sweep;

    public bool IsInUse(DateTime at)
    {
        lock (_gate)
        {
            if ((at - _checkedAt).TotalSeconds < 5) return _inUse;
            _checkedAt = at;
            return _inUse = _sweep();
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool Sweep()
    {
        var owned = new List<object?>();
        try
        {
            var enumerator = new MMDeviceEnumerator();
            owned.Add(enumerator);
            var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            owned.Add(endpoints);
            for (int d = 0; d < endpoints.Count; d++)
            {
                try
                {
                    var device = endpoints[d];
                    owned.Add(device);
                    var manager = device.AudioSessionManager;
                    owned.Add(manager);
                    var sessions = manager?.Sessions;
                    if (sessions == null) continue;
                    owned.Add(sessions);
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        var session = sessions[i];
                        owned.Add(session);
                        if (session.State == AudioSessionState.AudioSessionStateActive) return true;
                    }
                }
                catch { /* An endpoint may disappear during the sweep. */ }
            }
        }
        catch (Exception ex) { Serilog.Log.Debug("Awareness microphone probe failed: {Error}", ex.Message); }
        finally
        {
            for (int i = owned.Count - 1; i >= 0; i--)
                if (owned[i] is IDisposable disposable)
                    try { disposable.Dispose(); } catch { }
        }
        return false;
    }
}
