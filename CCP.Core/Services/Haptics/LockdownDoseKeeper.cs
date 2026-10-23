// Adapted from parity/trunk 4b79a20070a8deccecc5a2a7c63714a470815375.
// WPF dose rules; the head supplies engine, wall controls and UI dispatch.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Possession;
using Newtonsoft.Json;
namespace ConditioningControlPanel.Services.Haptics;
public sealed class LockdownDoseHost
{
    public Func<bool> IsEngineRunning = () => CoreEngine.IsRunning;
    public Action StartEngine = () => { };
    public Action StopEngine = () => { };
    public Action<string, bool> SetWallFeature = (_, _) => { };
    public Func<bool> IsSessionFeatureLockActive = () => false;
    public Action<string, int, bool> Bark = (_, _, _) => { };
    public Action<Action> OnUi = a => a();
    public Func<bool> IsAlive = () => true;
    internal TimeSpan KickoffDelay = TimeSpan.FromSeconds(2);
    internal TimeProvider Time = TimeProvider.System;   // test seam for the kickoff
    public Func<string> RecoveryPath = () => CoreLockdownDoseKeeper.DefaultRecoveryPath;
    public Func<string> AssetsPath = () => CorePaths.EffectiveAssets;
}
public sealed class CoreLockdownDoseKeeper : IDisposable
{
    public static CoreLockdownDoseKeeper? Current { get; set; }
    public static volatile Func<bool>? TakeoverRunningProvider;
    public sealed record DoseFeature(string Key, string Name, Func<AppSettings, bool> IsOn, int Tier);
    public static readonly IReadOnlyList<DoseFeature> Catalog = new DoseFeature[]
    {
        new("flash",        "Flash",            s => s.FlashEnabled,           0),
        new("subliminal",   "Subliminals",      s => s.SubliminalEnabled,      0),
        new("spiral",       "the Spiral",       s => s.SpiralEnabled,          0),
        new("pinkfilter",   "the Pink Filter",  s => s.PinkFilterEnabled,      0),
        new("bouncingtext", "Bouncing Text",    s => s.BouncingTextEnabled,    0),
        new("bubbles",      "Bubbles",          s => s.BubblesEnabled,         0),
        new("video",        "Mandatory Videos", s => s.MandatoryVideosEnabled, 1),
        new("mindwipe",     "Mind Wipe",        s => s.MindWipeEnabled,        2),
        new("lockcard",     "the Lock Card",    s => s.LockCardEnabled,        2),
        new("bubblecount",  "Bubble Count",     s => s.BubbleCountEnabled,     2),
        new("braindrain",   "Brain Drain",      s => s.BrainDrainEnabled,      2),
    };
    private static bool HasOffWallDose(AppSettings s, bool takeoverRunning) =>
        s.AudioOnlySession
        || s.PopQuizEnabled
        || (s.CornerGifOverlays?.Any(o => o != null && o.Enabled) == true)
        || (takeoverRunning && s.AutonomyModeEnabled && s.AutonomyConsentGiven);
    private static bool TakeoverIsRunning
    {
        get { try { return TakeoverRunningProvider?.Invoke() == true; } catch { return false; } }
    }
    public static bool CountsAsOffWallDose(AppSettings s, bool takeoverRunning)
        => s != null && HasOffWallDose(s, takeoverRunning);
    public static bool CountsAsOffWallDose(AppSettings s) => CountsAsOffWallDose(s, TakeoverIsRunning);
    public static bool DoseIsEmpty(AppSettings s, bool takeoverRunning)
    {
        if (s == null) return false;          // unknown = not our call; fail open
        if (HasOffWallDose(s, takeoverRunning)) return false;
        foreach (var f in Catalog)
        {
            try { if (f.IsOn(s)) return false; } catch { }
        }
        return true;
    }
    public static bool DoseIsEmpty(AppSettings s) => DoseIsEmpty(s, TakeoverIsRunning);
    public static List<string> KeysOn(AppSettings s)
    {
        var list = new List<string>();
        if (s == null) return list;
        foreach (var f in Catalog)
        {
            try { if (f.IsOn(s)) list.Add(f.Key); } catch { }
        }
        return list;
    }
    public static int WantedFor(int round) => Math.Clamp(1 + Math.Max(1, round), 2, 4);
    public static int DoseGraceFor(int roundsSoFar) => Math.Max(2, 6 - 2 * roundsSoFar);
    public const int EngineGraceSeconds = 4;
    public static List<string> PickConscripts(int round, IReadOnlyCollection<string> previouslyOn,
        IReadOnlyList<string> starter, IReadOnlyList<string> escalation,
        IReadOnlyCollection<string> currentlyOn, Random rng)
    {
        var want = WantedFor(round);
        var picked = new List<string>(want);
        var skip = new HashSet<string>(currentlyOn ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var known = new HashSet<string>(starter.Concat(escalation), StringComparer.OrdinalIgnoreCase);
        void TakeFrom(IEnumerable<string> pool)
        {
            foreach (var k in Shuffle(pool, rng))
            {
                if (picked.Count >= want) return;
                if (string.IsNullOrEmpty(k) || skip.Contains(k) || !known.Contains(k)) continue;
                if (picked.Contains(k, StringComparer.OrdinalIgnoreCase)) continue;
                picked.Add(k);
            }
        }
        TakeFrom(previouslyOn ?? Array.Empty<string>());
        TakeFrom(starter);
        if (round >= 2) TakeFrom(escalation);
        return picked;
    }
    private static List<string> Shuffle(IEnumerable<string> src, Random rng)
    {
        var list = src.ToList();
        for (int i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }
    private readonly LockdownService _lockdown;
    private readonly Random _rng = new();
    private bool _installed;
    private bool _armed;
    private bool _busy;
    private bool _disposed;
    private bool _panicHeld;
    public void HoldAfterPanic() => _panicHeld = true;
    /// <summary>WPF PanicAftermath.EndQuiet: only the user's own START undoes a panic.</summary>
    public void ReleasePanicHold() => _panicHeld = false;
    internal bool IsPanicHeld => _panicHeld;
    private int _round;                 // conscriptions so far this lockdown
    private int _engineIdle;            // consecutive ticks with the engine off
    private int _doseIdle;              // consecutive ticks with an empty dose
    private bool _wasEmpty;
    private bool _engineWasRunning;     // at activation
    private bool _weStartedEngine;
    private HashSet<string> _snapshotOn = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _flipped = new(StringComparer.OrdinalIgnoreCase);
    private System.Threading.ITimer? _kickoff;
    private readonly LockdownDoseHost _host;
    public int Round => _round;
    public bool IsArmed => _armed;
    public CoreLockdownDoseKeeper(LockdownService lockdown, LockdownDoseHost? host = null)
    {
        _lockdown = lockdown ?? throw new ArgumentNullException(nameof(lockdown));
        _host = host ?? new LockdownDoseHost();
    }
    public void Install()
    {
        if (_installed) return;
        _installed = true;
        _lockdown.LockdownActivated += OnActivated;
        _lockdown.LockdownDeactivated += OnDeactivated;
        _lockdown.CountdownTick += OnTick;
    }
    private static bool Enabled => CoreSettings.Current?.LockdownDoseKeeperEnabled != false;
    private void OnActivated()
    {
        try
        {
            if (!Enabled) { _armed = false; return; }
            var s = CoreSettings.Current;
            if (s == null) return;
            _armed = true;
            _panicHeld = false;
            _round = 0;
            _engineIdle = 0;
            _doseIdle = 0;
            _flipped.Clear();
            _snapshotOn = new HashSet<string>(KeysOn(s), StringComparer.OrdinalIgnoreCase);
            _wasEmpty = DoseIsEmpty(s);
            _engineWasRunning = _host.IsEngineRunning();
            _weStartedEngine = false;
            DeleteRecoveryFile();
            Serilog.Log.Information("Lockdown dose: armed (engine {Engine}, dose {Dose}: {Keys})",
                _engineWasRunning ? "running" : "OFF", _wasEmpty ? "EMPTY" : "ok", string.Join(",", _snapshotOn));
            StopKickoff();
            _kickoff = _host.Time.CreateTimer(_ => _host.OnUi(() =>
            {
                try { if (_armed && !_disposed) Enforce(atActivation: true); }
                catch (Exception ex) { Serilog.Log.Warning("Lockdown dose: kickoff failed: {Error}", ex.Message); }
            }), null, _host.KickoffDelay, System.Threading.Timeout.InfiniteTimeSpan);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning("Lockdown dose: arm failed: {Error}", ex.Message);
            _armed = false;
        }
    }
    private void OnTick(TimeSpan _)
    {
        if (!_armed || _disposed) return;
        if (!_host.IsAlive()) return;
        _host.OnUi(() =>
        {
            try { Enforce(atActivation: false); }
            catch (Exception ex) { Serilog.Log.Warning("Lockdown dose: tick failed: {Error}", ex.Message); }
        });
    }
    internal void Enforce(bool atActivation)
    {
        if (_busy || !_armed) return;
        if (!_lockdown.IsActive) { _armed = false; return; }
        if (!Enabled) return;   // turned off mid-lockdown: stand down, restore still runs at the end
        var s = CoreSettings.Current;
        if (s == null || !_host.IsAlive()) return;
        _busy = true;
        try
        {
            // Sessions own the dose; preserve _wasEmpty so their end can still trip starvation.
            if (_host.IsSessionFeatureLockActive())
            {
                _engineIdle = 0; _doseIdle = 0;
                return;
            }
            var empty = DoseIsEmpty(s);
            if (empty && !_wasEmpty && !atActivation)
            {
                try { _lockdown.NotifyEscapeAttempt(EscapeKinds.Starve); } catch { }
            }
            _wasEmpty = empty;
            if (!_host.IsEngineRunning())
            {
                if (_panicHeld) { _engineIdle = 0; return; }
                _engineIdle++;
                if (atActivation || _engineIdle >= EngineGraceSeconds)
                {
                    var names = empty ? Conscript(s) : null;
                    StartEngine();
                    _engineIdle = 0;
                    _doseIdle = 0;
                    Bark(names, engineStarted: true);
                }
                return;
            }
            _engineIdle = 0;
            if (!empty) { _doseIdle = 0; return; }
            _doseIdle++;
            if (atActivation || _doseIdle >= DoseGraceFor(_round))
            {
                var names = Conscript(s);
                _doseIdle = 0;
                Bark(names, engineStarted: false);
            }
        }
        finally { _busy = false; }
    }
    private string? Conscript(AppSettings s)
    {
        var round = _round + 1;
        var on = KeysOn(s);
        var starter = Catalog.Where(f => f.Tier == 0).Select(f => f.Key).ToList();
        var escalation = Catalog.Where(f => f.Tier == 1 && AssetsExistFor(f.Key)).Select(f => f.Key).ToList();
        var picks = PickConscripts(round, _snapshotOn, starter, escalation, on, _rng);
        if (picks.Count == 0)
        {
            Serilog.Log.Warning("Lockdown dose: round {Round} found nothing to conscript", round);
            return null;
        }
        _round = round;
        var names = new List<string>(picks.Count);
        foreach (var key in picks)
        {
            try
            {
                _host.SetWallFeature(key, true);
                _flipped.Add(key);
                names.Add(Catalog.First(f => f.Key == key).Name);
            }
            catch (Exception ex) { Serilog.Log.Warning("Lockdown dose: could not switch {Key} on: {Error}", key, ex.Message); }
        }
        WriteRecoveryFile();
        Serilog.Log.Information("Lockdown dose: round {Round} conscripted {Keys}", round, string.Join(",", picks));
        return names.Count == 0 ? null : JoinNames(names);
    }
    private static string JoinNames(List<string> names) => names.Count switch
    {
        0 => "",
        1 => names[0],
        2 => names[0] + " and " + names[1],
        _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1],
    };
    private void StartEngine()
    {
        try
        {
            _host.StartEngine();
            _weStartedEngine = true;
            WriteRecoveryFile();
            Serilog.Log.Information("Lockdown dose: engine started for the user");
        }
        catch (Exception ex) { Serilog.Log.Warning("Lockdown dose: StartEngine failed: {Error}", ex.Message); }
    }
    private void Bark(string? names, bool engineStarted)
    {
        try { _host.Bark(string.IsNullOrEmpty(names) ? "something" : names, _round, engineStarted); } catch { }   // WPF BarkService.cs:422
    }
    private static readonly string[] VideoExtensions = { ".mp4", ".mov", ".avi", ".wmv", ".mkv", ".webm" };
    private bool AssetsExistFor(string key)
    {
        try
        {
            if (key == "video")
            {
                var dir = Path.Combine(_host.AssetsPath(), "videos");
                if (!Directory.Exists(dir)) return false;
                return Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
                                .Any(f => VideoExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
            }
        }
        catch { }
        return true;
    }
    public static bool IsPlayableVideoFile(string path)
    {
        try { return !string.IsNullOrWhiteSpace(path) && VideoExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()); }
        catch { return false; }
    }
    private void OnDeactivated()
    {
        try
        {
            StopKickoff();
            if (!_armed) { DeleteRecoveryFile(); return; }   // nothing was ever borrowed this lockdown
            _armed = false;
            Restore();
        }
        catch (Exception ex) { Serilog.Log.Warning("Lockdown dose: restore failed: {Error}", ex.Message); }
    }
    private void Restore()
    {
        if (!_host.IsAlive())
        {
            Serilog.Log.Warning("Lockdown dose: no main window at restore - {Count} toggle(s) left on, "
                + "recovery file kept for the next launch", _flipped.Count);
            return;
        }
        _host.OnUi(RestoreOnUi);
    }
    private void RestoreOnUi()
    {
        if (!_host.IsAlive()) return;
        var gaveBack = new List<string>();
        var stillFlipped = new List<string>();
        foreach (var key in _flipped.ToList())
        {
            if (_snapshotOn.Contains(key)) continue;   // they had it on themselves - leave it
            try { _host.SetWallFeature(key, false); gaveBack.Add(key); }
            catch (Exception ex)
            {
                stillFlipped.Add(key);
                Serilog.Log.Warning("Lockdown dose: could not give back {Key}: {Error}", key, ex.Message);
            }
        }
        _flipped.Clear();
        foreach (var key in stillFlipped) _flipped.Add(key);
        var stoppedEngine = false;
        if (_weStartedEngine && !_engineWasRunning && _host.IsEngineRunning() && !_host.IsSessionFeatureLockActive())
        {
            try { _host.StopEngine(); stoppedEngine = true; }
            catch (Exception ex) { Serilog.Log.Warning("Lockdown dose: StopEngine failed: {Error}", ex.Message); }
        }
        Serilog.Log.Information("Lockdown dose: released (gave back {Keys}; engine {Engine})",
            gaveBack.Count == 0 ? "nothing" : string.Join(",", gaveBack), stoppedEngine ? "stopped" : "left as is");
        // Keep the recovery record until every borrowed toggle is actually given back.
        if (stillFlipped.Count == 0) DeleteRecoveryFile();
        else WriteRecoveryFile();
    }
    private sealed class RecoveryState
    {
        public List<string> Flipped { get; set; } = new();
    }
    public static string DefaultRecoveryPath => Path.Combine(CorePaths.UserData, "lockdown-dose.json");
    private string RecoveryPath => _host.RecoveryPath();
    private void WriteRecoveryFile()
    {
        try
        {
            var toRestore = _flipped.Where(k => !_snapshotOn.Contains(k)).ToList();
            if (toRestore.Count == 0) { DeleteRecoveryFile(); return; }
            File.WriteAllText(RecoveryPath, JsonConvert.SerializeObject(new RecoveryState { Flipped = toRestore }));
        }
        catch (Exception ex) { Serilog.Log.Debug("Lockdown dose: recovery write failed: {Error}", ex.Message); }
    }
    private void DeleteRecoveryFile() => DeleteRecoveryFile(RecoveryPath);
    private static void DeleteRecoveryFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
    private void StopKickoff()
    {
        try { _kickoff?.Dispose(); } catch { }
        _kickoff = null;
    }
    public static void RecoverIfNeeded(string? path = null)
    {
        var recoveryPath = path ?? DefaultRecoveryPath;
        try
        {
            if (!File.Exists(recoveryPath)) return;
            var state = JsonConvert.DeserializeObject<RecoveryState>(File.ReadAllText(recoveryPath));
            var s = CoreSettings.Current;
            if (state?.Flipped != null && s != null)
            {
                foreach (var key in state.Flipped)
                {
                    switch (key)
                    {
                        case "flash": s.FlashEnabled = false; break;
                        case "subliminal": s.SubliminalEnabled = false; break;
                        case "spiral": s.SpiralEnabled = false; break;
                        case "pinkfilter": s.PinkFilterEnabled = false; break;
                        case "bouncingtext": s.BouncingTextEnabled = false; break;
                        case "bubbles": s.BubblesEnabled = false; break;
                        case "video": s.MandatoryVideosEnabled = false; break;
                    }
                }
                try { CoreSettings.SaveImmediate(); } catch { }
                Serilog.Log.Information("Lockdown dose recovery: switched {Keys} back off after an interrupted lockdown",
                    string.Join(",", state.Flipped));
            }
        }
        catch (Exception ex) { Serilog.Log.Warning("Lockdown dose recovery failed: {Error}", ex.Message); }
        finally { DeleteRecoveryFile(recoveryPath); }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopKickoff();
        if (_installed)
        {
            _lockdown.LockdownActivated -= OnActivated;
            _lockdown.LockdownDeactivated -= OnDeactivated;
            _lockdown.CountdownTick -= OnTick;
        }
    }
}
