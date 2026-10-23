using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>One live WASAPI session: its PID and its own (per-app mixer) volume.</summary>
    internal sealed record DuckSession(int Pid, Func<float> GetVolume, Action<float> SetVolume);

    /// <summary>
    /// Windows ducking, ported from WPF AudioService (Duck / ApplyDuckSweep / RescanSweep / Unduck /
    /// RestoreDuckedVolumes / RestoreRetrySweep / DuckWatchdogTick / ForceUnduck). Same rules: a ref count
    /// and a generation, "ducked" decided synchronously, every session on every active render endpoint
    /// lowered to volume x (1 - strength%) at once (WPF has no fade), never our own process, PID 0 or
    /// (setting ExcludeBambiCloudFromDucking, default on) a WebView2 process; a 2.5 s rescan for sessions
    /// born during the duck, a 5 min watchdog, restores that cannot land retried every 5 s for 3 min,
    /// and our own layers ducked in-process. Every original not yet handed back is journalled in WPF's
    /// ducking_recovery.json shape BEFORE any volume drops, so DuckRecovery restores it after a crash.
    /// All COM work runs on one serialized worker, never the caller's thread.
    /// </summary>
    internal sealed class WindowsDucker
    {
        internal const int WatchdogMs = 300_000, RescanMs = 2_500, RetryMs = 5_000;
        private static readonly TimeSpan RetryWindow = TimeSpan.FromMinutes(3);

        private readonly Func<Func<IReadOnlyList<DuckSession>, int>, int> _sweep;
        private readonly Func<int, string> _nameOf;
        private readonly string? _journalDir;
        private readonly Func<bool> _excludeWebView2;
        private readonly int _ownPid;

        private readonly object _lock = new();
        private readonly Dictionary<int, float> _originals = new();
        private readonly Dictionary<int, float> _pending = new();
        private readonly Dictionary<int, string> _names = new();
        private int _count;
        private bool _ducked;
        private float _amount;
        private long _generation;
        private Timer? _watchdog, _rescan, _retry;
        private DateTime _retryDeadline;
        private Task _work;
        private int _depth;

        /// <param name="sweep">Runs one sweep body over the live sessions and releases them after
        /// (WASAPI in the app, a stub in tests). <paramref name="after"/>: work to finish first (the launch recovery).</param>
        internal WindowsDucker(Func<Func<IReadOnlyList<DuckSession>, int>, int> sweep, Func<int, string> nameOf, string? journalDir,
            Func<bool> excludeWebView2, int ownPid, Task? after = null)
        {
            _sweep = sweep;
            _nameOf = nameOf;
            _journalDir = journalDir;
            _excludeWebView2 = excludeWebView2;
            _ownPid = ownPid;
            _work = after ?? Task.CompletedTask;
        }

        /// <summary>The app's ducker: WASAPI sessions, WPF's journal under <paramref name="userData"/>, after the launch recovery.</summary>
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        internal static WindowsDucker ForApp(string userData) => new(body =>
            {
                using var scope = WasapiSessions.Collect();
                return body(scope.Sessions);
            }, ProcessName, userData,
            () => CoreSettings.Current?.ExcludeBambiCloudFromDucking ?? true, Environment.ProcessId, DuckRecovery.LaunchRecovery);

        internal long Generation { get { lock (_lock) return _generation; } }
        internal bool IsDucked { get { lock (_lock) return _ducked; } }

        public void Duck(int strength)
        {
            if ((CoreSettings.Current?.MasterVolume ?? 100) == 0) return; // as WPF: nothing of ours will play
            float amount;
            lock (_lock)
            {
                _count++;
                if (_ducked) return;
                _amount = amount = Math.Clamp(strength, 0, 100) / 100f;
                _ducked = true;
                _watchdog?.Dispose();
                _watchdog = new Timer(_ => WatchdogTick(), null, WatchdogMs, WatchdogMs);
                _rescan?.Dispose();
                _rescan = new Timer(_ => Rescan(), null, RescanMs, RescanMs);
            }
            try { LayeredAudio.Instance?.ApplyDuckGain(amount); } catch { }   // never ApplyDuck here: Volume is the whole session
            Enqueue(() => DuckSweep(rescan: false));
        }

        /// <summary>WPF RescanForNewSessions: skip, never queue behind, a sweep still in flight (#750-#753).</summary>
        internal void Rescan()
        {
            if (Volatile.Read(ref _depth) == 0 && IsDucked) Enqueue(() => DuckSweep(rescan: true));
        }

        public void Unduck(long generation)
        {
            lock (_lock)
            {
                if (generation >= 0 && generation != _generation) return; // stale callback
                _count = Math.Max(0, _count - 1);
                if (!_ducked || _count > 0) return;
                _ducked = false;
                DisposeDuckTimers();
            }
            try { LayeredAudio.Instance?.ReleaseDuckGain(); } catch { }
            Enqueue(Restore);
        }

        /// <summary>Panic / exit: advancing the generation turns every pending Unduck into a stale no-op.</summary>
        internal void ForceUnduck()
        {
            long gen;
            lock (_lock) { _generation++; _count = 1; gen = _generation; }
            Unduck(gen);
        }

        /// <summary>App exit: hand everything back within WPF's 4 s flush budget.</summary>
        internal void Shutdown()
        {
            if (IsDucked) ForceUnduck();
            Drain(4000);
            lock (_lock) { _retry?.Dispose(); _retry = null; }
        }

        internal void Drain(int ms = 2000) { try { _work.Wait(ms); } catch { } }

        private void DisposeDuckTimers()
        {
            _watchdog?.Dispose(); _watchdog = null;
            _rescan?.Dispose(); _rescan = null;   // #686: a surviving rescan re-ducks the machine forever
        }

        private void Enqueue(Action work)
        {
            Interlocked.Increment(ref _depth);
            lock (_lock)
                _work = _work.ContinueWith(_ =>
                {
                    try { work(); }
                    catch (Exception ex) { Log.Debug("[Ducking] sweep failed: {E}", ex.Message); }
                    finally { Interlocked.Decrement(ref _depth); }
                }, TaskScheduler.Default);
        }

        private int Sweep(Func<IReadOnlyList<DuckSession>, int> body) => _sweep(body);

        /// <summary>WPF ApplyDuckSweep / RescanSweep. Two passes: capture and journal every original, then lower.</summary>
        private void DuckSweep(bool rescan)
        {
            var webView = _excludeWebView2();
            var ducked = Sweep(sessions =>
            {
                float amount;
                var toLower = new List<(DuckSession S, float Original)>();
                lock (_lock) { if (!_ducked) return 0; amount = _amount; }
                foreach (var s in sessions)
                {
                    try
                    {
                        if (s.Pid == _ownPid || s.Pid == 0) continue;
                        var name = _nameOf(s.Pid);
                        if (webView && name.Contains("webview2", StringComparison.OrdinalIgnoreCase)) continue;
                        var volume = s.GetVolume();
                        if (rescan && volume <= 0.001f) continue;   // WPF: likely a session we ducked under a recycled PID
                        lock (_lock)
                        {
                            if (!_ducked) return 0;
                            // Never overwrite a surviving entry: it is the TRUE original (volumes ratcheted to 0 otherwise).
                            if (_originals.ContainsKey(s.Pid)) continue;
                            // A parked restore still owes this app its TRUE original: that stays the original,
                            // never the live (still ducked) level, or the journal and the unduck lose it.
                            if (_pending.Remove(s.Pid, out var owed)) volume = owed;
                            _originals[s.Pid] = volume;
                            _names[s.Pid] = name;
                        }
                        toLower.Add((s, volume));
                    }
                    catch { /* the session may have ended */ }
                }
                if (toLower.Count == 0) return 0;
                Journal();   // before any volume drops: a crash in between must still find the originals
                var n = 0;
                foreach (var (s, original) in toLower)
                {
                    try { s.SetVolume(Math.Max(0f, original * (1f - amount))); n++; }
                    catch { /* the session may have ended */ }
                }
                return n;
            });
            if (ducked > 0) Log.Information(rescan ? "[Ducking] Rescan ducked {Count} new session(s)" : "Ducked {Count} audio sessions", ducked);
        }

        /// <summary>WPF RestoreDuckedVolumes + RestoreRetrySweep: same PID, else the same app by name (DuckRecoveryRule.Match).</summary>
        private void Restore() => HandBack(retry: false);

        private void HandBack(bool retry)
        {
            Dictionary<int, float> wanted;
            Dictionary<int, string> names;
            lock (_lock)
            {
                wanted = new Dictionary<int, float>(retry ? _pending : _originals);
                names = new Dictionary<int, string>(_names);
            }
            if (wanted.Count == 0) { Journal(); return; }
            HashSet<int> busy;
            lock (_lock) busy = retry ? new HashSet<int>(_originals.Keys) : new HashSet<int>();
            var restored = new HashSet<int>();
            try
            {
                Sweep(sessions =>
                {
                    void Set(int pid, float volume, int stored)
                    {
                        foreach (var s in sessions)
                            if (s.Pid == pid) try { s.SetVolume(volume); restored.Add(stored); } catch { }
                    }
                    // Same PID first.
                    foreach (var (pid, volume) in wanted)
                        if (pid != _ownPid && pid != 0) Set(pid, volume, pid);
                    // WPF: the name fallback looks only at sessions that are not themselves owed (nor, on a
                    // retry, held ducked right now), so a gone entry never lands on an app with its own entry.
                    var nameToPid = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    foreach (var s in sessions)
                    {
                        if (s.Pid == 0 || s.Pid == _ownPid || wanted.ContainsKey(s.Pid) || busy.Contains(s.Pid)) continue;
                        try { var n = _nameOf(s.Pid); if (n.Length > 0) nameToPid.TryAdd(n, s.Pid); } catch { }
                    }
                    foreach (var (pid, volume) in wanted)
                    {
                        if (restored.Contains(pid) || !names.TryGetValue(pid, out var name) || string.IsNullOrEmpty(name)) continue;
                        if (!nameToPid.Remove(name, out var fresh)) continue;
                        Set(fresh, volume, pid);
                    }
                    return restored.Count;
                });
            }
            catch (Exception ex)
            {
                // WPF: keep the originals (re-reading ducked volumes as originals ratchets them to 0) and
                // pin the duck on with one ref so the watchdog retries.
                Log.Warning("Audio unducking failed, preserving state for retry: {Error}", ex.Message);
                if (retry) return;
                lock (_lock)
                {
                    _ducked = true;
                    _count = 1;
                    _watchdog ??= new Timer(_ => WatchdogTick(), null, WatchdogMs, WatchdogMs);
                }
                return;
            }
            lock (_lock)
            {
                foreach (var pid in wanted.Keys)
                {
                    if (retry) { if (restored.Contains(pid)) _pending.Remove(pid); continue; }
                    _originals.Remove(pid);
                    if (!restored.Contains(pid)) _pending[pid] = wanted[pid];
                }
                if (_pending.Count == 0) { _names.Clear(); _retry?.Dispose(); _retry = null; }
                else if (!retry)
                {
                    _retryDeadline = DateTime.UtcNow + RetryWindow;
                    _retry?.Dispose();
                    _retry = new Timer(_ => RetryTick(), null, RetryMs, RetryMs);
                }
            }
            Journal();   // parked restores stay in the file: an exit inside the retry window still heals at launch
        }

        private void RetryTick()
        {
            lock (_lock)
            {
                if (_pending.Count == 0 || DateTime.UtcNow > _retryDeadline)
                {
                    // Stop retrying, keep them: the journal hands them back at the next launch.
                    _retry?.Dispose(); _retry = null;
                    return;
                }
            }
            if (Volatile.Read(ref _depth) == 0) Enqueue(() => HandBack(retry: true));
        }

        private void WatchdogTick()
        {
            lock (_lock)
            {
                if (!_ducked) { _watchdog?.Dispose(); _watchdog = null; return; }
                Log.Warning("[Ducking] Watchdog fired after {Ms}ms - force-unducking to prevent stuck volume", WatchdogMs);
                _ducked = false;
                _count = 0;
                _generation++;
                DisposeDuckTimers();
            }
            try { LayeredAudio.Instance?.ReleaseDuckGain(); } catch { }
            Enqueue(Restore);
        }

        /// <summary>WPF SaveDuckingState: every original not yet handed back, the live ducks and the parked ones.</summary>
        private void Journal()
        {
            if (_journalDir is not { } dir) return;
            Dictionary<int, float> owed;
            Dictionary<int, string> names;
            lock (_lock)
            {
                owed = global::ConditioningControlPanel.Services.Audio.DuckRecoveryRule.ToPersist(_originals, _pending);
                names = new Dictionary<int, string>(_names);
            }
            DuckRecovery.SaveWpfJournal(dir, owed, names);
        }

        private static string ProcessName(int pid)
        {
            try { using var p = Process.GetProcessById(pid); return p.ProcessName ?? ""; }
            catch { return ""; }
        }
    }
}
