using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using ConditioningControlPanel.Services.Audio;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// Ducking crash recovery (WPF AudioService RecoverFromCrash / SaveDuckingState + Core DuckRecoveryRule).
    /// If the app dies while other apps are ducked they stay quiet, and both Windows and PulseAudio
    /// (module-stream-restore) remember that volume per app. So every original not yet handed back is
    /// journalled, and the next launch puts it back.
    /// <list type="bullet">
    /// <item>Linux: this head ducks other apps' sink-inputs through pactl (LibVlcAudio). Its journal is
    /// <see cref="PulseFileName"/>, one <see cref="PulseStream"/> per sink-input index, restored at
    /// launch on the same pactl queue before any new duck.</item>
    /// <item>Windows: WindowsDucker (WPF's WASAPI ducker) journals in WPF's own shape,
    /// <see cref="WpfFileName"/> + names, so a 7.1.x journal (crash, then the 7.2 upgrade) and this
    /// head's own are restored the same way at launch, over WASAPI.</item>
    /// </list>
    /// </summary>
    internal static class DuckRecovery
    {
        internal const string PulseFileName = "ducking_recovery_pulse.json";
        internal const string WpfFileName = "ducking_recovery.json";
        internal const string WpfNamesFileName = "ducking_recovery_names.json";

        /// <summary>One ducked sink-input: its owner (PID 0 when pulse does not say) and raw channel volumes.</summary>
        internal sealed record PulseStream(int Pid, string Name, string[] Volumes);

        /// <summary>One live sink-input from <c>pactl -f json list sink-inputs</c>.</summary>
        internal sealed record SinkInput(int Index, int Pid, string Name, int[] Volumes);

        internal static List<SinkInput> ParseSinkInputs(string json)
        {
            var list = new List<SinkInput>();
            using var doc = JsonDocument.Parse(json);
            foreach (var si in doc.RootElement.EnumerateArray())
            {
                var props = si.GetProperty("properties");
                string Prop(string name) => props.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                var pid = int.TryParse(Prop("application.process.id"), out var p) ? p : 0;
                var name = Prop("application.process.binary");
                if (name.Length == 0) name = Prop("application.name");
                var raw = si.GetProperty("volume").EnumerateObject().Select(c => c.Value.GetProperty("value").GetInt32()).ToArray();
                list.Add(new SinkInput(si.GetProperty("index").GetInt32(), pid, name, raw));
            }
            return list;
        }

        // ---- the pulse journal ------------------------------------------------------------------

        /// <summary>Writes what is owed; nothing owed = no file (WPF SaveDuckingState / ClearDuckingState).</summary>
        internal static void SavePulse(string path, IReadOnlyDictionary<int, PulseStream> owed)
        {
            try
            {
                if (owed.Count == 0) { if (File.Exists(path)) File.Delete(path); return; }
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(owed));
            }
            catch (Exception ex) { Log.Debug("Failed to save ducking state: {Error}", ex.Message); }
        }

        internal static Dictionary<int, PulseStream> LoadPulse(string path)
        {
            try
            {
                if (File.Exists(path))
                    return JsonSerializer.Deserialize<Dictionary<int, PulseStream>>(File.ReadAllText(path)) ?? new();
            }
            catch (Exception ex) { Log.Warning("Unreadable ducking recovery file: {Error}", ex.Message); }
            return new();
        }

        /// <summary>
        /// Launch: give every journalled stream its original volume back, then drop the journal (as WPF,
        /// once tried; an app not playing now is not chased). The same PID and name, else the same app
        /// by name after a restart (DuckRecoveryRule.Match); our own process never.
        /// </summary>
        internal static int RecoverPulse(string path, Func<string, string> pactl, int ownPid)
        {
            var owed = LoadPulse(path);
            if (owed.Count == 0) { SavePulse(path, owed); return 0; }
            Log.Information("Detected ducking recovery file - restoring {Count} streams from a previous crash", owed.Count);
            var restored = 0;
            try
            {
                var live = ParseSinkInputs(pactl("-f json list sink-inputs")).Where(s => s.Pid != ownPid).ToList();
                var names = new Dictionary<int, string>();
                foreach (var s in live) if (s.Pid != 0) names.TryAdd(s.Pid, s.Name);
                var done = new HashSet<int>();
                foreach (var (index, stream) in owed)
                {
                    var target = DuckRecoveryRule.Match(stream.Pid, stream.Name, names, ownPid);
                    // The app's streams; the very stream when it is still there. Pulse sometimes names no
                    // PID: then only the same index under the same name.
                    var hits = target != null
                        ? live.Where(s => s.Pid == target).ToList()
                        : live.Where(s => s.Pid == 0 && s.Index == index && s.Name.Length > 0 && s.Name == stream.Name).ToList();
                    if (hits.Any(s => s.Index == index)) hits = hits.Where(s => s.Index == index).ToList();
                    foreach (var s in hits)
                    {
                        if (!done.Add(s.Index)) continue;
                        try { pactl($"set-sink-input-volume {s.Index} {VolumeArgs(stream.Volumes, s.Volumes.Length)}"); restored++; }
                        catch { /* the stream ended meanwhile */ }
                    }
                }
                Log.Information("Restored {Count} audio streams from crash recovery", restored);
            }
            catch (Exception ex) { Log.Warning("Failed to recover ducking state: {Error}", ex.Message); }
            SavePulse(path, new Dictionary<int, PulseStream>());
            return restored;
        }

        /// <summary>pactl takes one volume per channel or one for all: a restarted stream may have a different layout.</summary>
        internal static string VolumeArgs(string[] stored, int channels)
            => stored.Length == channels || stored.Length == 1 ? string.Join(' ', stored)
                : stored.Select(v => int.TryParse(v, out var n) ? n : 0).DefaultIfEmpty(0).Max().ToString();

        // ---- the WPF journal (Windows) ----------------------------------------------------------

        /// <summary>
        /// Restores what a WPF build journalled before it died, then deletes both files (WPF
        /// RecoverFromCrashSweep, line for line). <paramref name="sessions"/> lists live WASAPI sessions
        /// as (PID, set volume); <paramref name="nameOf"/> names a PID ("" when unknown).
        /// </summary>
        internal static int RecoverWpfJournal(string userData, Func<IReadOnlyList<(int Pid, Action<float> SetVolume)>> sessions,
            Func<int, string> nameOf, int ownPid)
        {
            var file = Path.Combine(userData, WpfFileName);
            var namesFile = Path.Combine(userData, WpfNamesFileName);
            if (!File.Exists(file)) return 0;
            var restored = 0;
            try
            {
                Log.Information("Detected ducking recovery file - restoring audio from previous crash");
                var saved = JsonSerializer.Deserialize<Dictionary<int, float>>(File.ReadAllText(file));
                Dictionary<int, string>? savedNames = null;
                try { if (File.Exists(namesFile)) savedNames = JsonSerializer.Deserialize<Dictionary<int, string>>(File.ReadAllText(namesFile)); }
                catch { /* an unreadable names file only loses the name fallback */ }
                if (saved is { Count: > 0 })
                {
                    var all = sessions();
                    var live = new Dictionary<int, string>();
                    foreach (var s in all) if (s.Pid != 0 && !live.ContainsKey(s.Pid)) live[s.Pid] = nameOf(s.Pid);
                    foreach (var (pid, volume) in saved)
                    {
                        string? name = null;
                        savedNames?.TryGetValue(pid, out name);
                        var target = DuckRecoveryRule.Match(pid, name, live, ownPid);
                        if (target == null) continue;
                        foreach (var s in all)
                        {
                            if (s.Pid != target.Value) continue;
                            try { s.SetVolume(volume); restored++; } catch { /* the session may have ended */ }
                        }
                    }
                    Log.Information("Restored {Count} audio sessions from crash recovery", restored);
                }
            }
            catch (Exception ex) { Log.Warning("Failed to recover ducking state: {Error}", ex.Message); }
            try { File.Delete(file); } catch { }
            try { File.Delete(namesFile); } catch { }
            return restored;
        }

        /// <summary>WPF SaveDuckingState's shape: PID -> original volume, names beside it; nothing owed = no files.</summary>
        internal static void SaveWpfJournal(string userData, IReadOnlyDictionary<int, float> owed, IReadOnlyDictionary<int, string> names)
        {
            var file = Path.Combine(userData, WpfFileName);
            var namesFile = Path.Combine(userData, WpfNamesFileName);
            try
            {
                if (owed.Count == 0)
                {
                    if (File.Exists(file)) File.Delete(file);
                    if (File.Exists(namesFile)) File.Delete(namesFile);
                    return;
                }
                Directory.CreateDirectory(userData);
                File.WriteAllText(file, JsonSerializer.Serialize(owed));
                var known = new Dictionary<int, string>();
                foreach (var pid in owed.Keys)
                    if (names.TryGetValue(pid, out var n) && !string.IsNullOrEmpty(n)) known[pid] = n;
                File.WriteAllText(namesFile, JsonSerializer.Serialize(known));
            }
            catch (Exception ex) { Log.Debug("Failed to save ducking state: {Error}", ex.Message); }
        }

        /// <summary>The launch restore of the WPF journal. The Windows ducker queues behind it, so a new
        /// duck never rewrites the file before it is read.</summary>
        internal static System.Threading.Tasks.Task LaunchRecovery { get; private set; } = System.Threading.Tasks.Task.CompletedTask;

        /// <summary>Windows launch: the WPF journal over WASAPI, off the UI thread (WPF ran it on its duck worker).</summary>
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        internal static void RecoverWpfJournalAtLaunch(string userData)
        {
            if (!File.Exists(Path.Combine(userData, WpfFileName))) return;
            LaunchRecovery = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    using var scope = WasapiSessions.Collect();
                    RecoverWpfJournal(userData, () => scope.Sessions.Select(s => (s.Pid, s.SetVolume)).ToList(), ProcessName, Environment.ProcessId);
                }
                catch (Exception ex) { Log.Warning("Ducking recovery over WASAPI failed: {Error}", ex.Message); }
            });
        }

        private static string ProcessName(int pid)
        {
            try { using var p = Process.GetProcessById(pid); return p.ProcessName; }
            catch { return ""; }
        }
    }
}
