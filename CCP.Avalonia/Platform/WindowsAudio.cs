using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Runtime.Versioning;
using NAudio.CoreAudioApi;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF AudioService's WASAPI endpoint/session access. No COM wrapper escapes a sweep.</summary>
internal sealed class WindowsAudio
{
    internal sealed record Session(int Pid, string Name, Func<float> Read, Action<float> Write);
    private readonly Action<Action<Session>> _visit;
    private readonly Dictionary<int, (float Volume, string Name)> _original = new();
    private readonly string? _recoveryFile;
    internal bool HasPendingRestores => _original.Count > 0;

    internal WindowsAudio(Action<Action<Session>> visit, string? recoveryFile = null)
    { _visit = visit; _recoveryFile = recoveryFile; }

    [SupportedOSPlatform("windows")]
    internal static WindowsAudio Create() => new(VisitSessions, Path.Combine(CorePaths.UserData, "ducking_recovery.json"));

    // Called only by LibVlcAudio's serialized duck worker; its epoch check guards retired sweeps.
    internal void Sweep(double keep, bool rescan, bool excludeWeb, Func<bool> active)
    {
        try { _visit(session =>
        {
            try
            {
                if (!active() || session.Pid == 0 || session.Pid == Environment.ProcessId
                    || (excludeWeb && session.Name.Contains("webview2", StringComparison.OrdinalIgnoreCase))
                    || _original.ContainsKey(session.Pid)) return;
                var volume = session.Read();
                if (rescan && CoreAudio.IsSilentForDuckRescan(volume)) return;
                // Preserve the original even if the endpoint throws during the write.
                _original.Add(session.Pid, (volume, session.Name));
                session.Write(Math.Max(0, volume * (float)keep));
            }
            catch (Exception ex) { Log.Debug(ex, "[Audio] WASAPI session ended during duck"); }
        }); }
        catch (Exception ex) { Log.Debug(ex, "[Audio] Render endpoints unavailable during duck"); }
        Save();
    }

    internal void Restore()
    {
        var restored = new HashSet<int>();
        try { _visit(session =>
        {
            try
            {
                if (session.Pid == 0 || session.Pid == Environment.ProcessId) return;
                if (_original.TryGetValue(session.Pid, out var saved))
                {
                    session.Write(saved.Volume);
                    restored.Add(session.Pid);
                    return;
                }
                // WPF: a restarted application can have a different PID at restore time.
                foreach (var pair in _original)
                {
                    if (restored.Contains(pair.Key) || pair.Value.Name.Length == 0
                        || !string.Equals(pair.Value.Name, session.Name, StringComparison.OrdinalIgnoreCase)) continue;
                    session.Write(pair.Value.Volume);
                    restored.Add(pair.Key);
                    break;
                }
            }
            catch (Exception ex) { Log.Debug(ex, "[Audio] WASAPI restore retained for retry"); }
        }); }
        catch (Exception ex) { Log.Debug(ex, "[Audio] Render endpoints unavailable during restore"); }
        foreach (var pid in restored) _original.Remove(pid);
        Save();
    }

    // Same PID-only recovery format and own/system guards as WPF RecoverFromCrashSweep.
    internal void Recover()
    {
        if (_recoveryFile == null || !File.Exists(_recoveryFile)) return;
        try
        {
            var saved = JsonSerializer.Deserialize<Dictionary<int, float>>(File.ReadAllText(_recoveryFile));
            if (saved != null)
                foreach (var pair in saved) _original[pair.Key] = (Math.Clamp(pair.Value, 0, 1), "");
            Restore();
        }
        catch (Exception ex) { Log.Debug(ex, "[Audio] Could not recover Windows duck state"); }
        finally
        {
            _original.Clear(); // WPF does not retry crash recovery for vanished processes.
            try { File.Delete(_recoveryFile); } catch { }
        }
    }

    private void Save()
    {
        if (_recoveryFile == null) return; // fake-session tests are memory-only
        try
        {
            if (_original.Count == 0) { File.Delete(_recoveryFile); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(_recoveryFile)!);
            File.WriteAllText(_recoveryFile, JsonSerializer.Serialize(_original.ToDictionary(p => p.Key, p => p.Value.Volume)));
        }
        catch (Exception ex) { Log.Debug(ex, "[Audio] Could not save Windows duck state"); }
    }

    [SupportedOSPlatform("windows")]
    internal static void EnumerateDevices(Action<LibVlcAudio.OutputDevice> add)
    {
        using var enumerator = new MMDeviceEnumerator();
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        for (var i = 0; i < devices.Count; i++)
        {
            try
            {
                using var device = devices[i];
                add(new(device.ID ?? "", device.FriendlyName ?? "(unnamed)"));
            }
            catch (Exception ex) { Log.Debug(ex, "[Audio] Unreadable playback endpoint"); }
        }
    }

    [SupportedOSPlatform("windows")]
    private static void VisitSessions(Action<Session> visit)
    {
        using var enumerator = new MMDeviceEnumerator();
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        for (var d = 0; d < devices.Count; d++)
        {
            try
            {
                using var device = devices[d]; // owns its AudioSessionManager
                var sessions = device.AudioSessionManager.Sessions;
                try
                {
                    for (var i = 0; i < sessions.Count; i++)
                    {
                        try
                        {
                            using var session = sessions[i];
                            var pid = (int)session.GetProcessID;
                            var name = "";
                            try { using var process = Process.GetProcessById(pid); name = process.ProcessName; }
                            catch { /* system or exited process */ }
                            visit(new(pid, name, () => session.SimpleAudioVolume.Volume,
                                value => session.SimpleAudioVolume.Volume = value));
                        }
                        catch (Exception ex) { Log.Debug(ex, "[Audio] Unreadable audio session"); }
                    }
                }
                finally { (sessions as IDisposable)?.Dispose(); }
            }
            catch (Exception ex) { Log.Debug(ex, "[Audio] Unreadable render endpoint"); }
        }
    }
}
