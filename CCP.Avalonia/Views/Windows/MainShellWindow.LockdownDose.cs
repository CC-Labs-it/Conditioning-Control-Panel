// WPF Services/Haptics/LockdownDoseKeeper.cs:52-70,107-190,227-260,282-380,397-409,448-575.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Possession;
using ConditioningControlPanel.Services.Safety;
using Newtonsoft.Json;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private bool _doseEngineArmed, _doseEngineWasRunning, _doseEngineRequested, _doseEngineStarted, _doseWasEmpty;
    private int _doseEngineIdle, _doseEngineGeneration, _doseIdle, _doseRound;
    private DispatcherTimer? _doseEngineKickoff;
    private readonly HashSet<string> _doseSnapshot = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _doseFlipped = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string[] DoseStarter = { "flash", "subliminal", "spiral", "pinkfilter", "bouncingtext", "bubbles" };
    private static readonly string[] DoseWall = { "flash", "subliminal", "spiral", "pinkfilter", "bouncingtext", "bubbles", "video", "mindwipe", "lockcard", "bubblecount", "braindrain" };
    private static string DoseRecoveryPath => Path.Combine(CorePaths.UserData, "lockdown-dose.json");
    // Headless tests can exercise a desktop-capable backend without opening real overlays.
    internal bool? DoseOverlaySupportForTests, DoseVideoReadyForTests;

    private void InitializeLockdownDoseEngine()
    {
        if (LockdownService.Current is not { } ld) return;
        void OnUi(Action action)
        {
            if (Dispatcher.UIThread.CheckAccess()) action();
            else Dispatcher.UIThread.Post(action);
        }
        void Activate() => OnUi(() =>
        {
            if (!ld.IsActive || !CoreSettings.Current.LockdownDoseKeeperEnabled) return;
            if (File.Exists(DoseRecoveryPath))
            {
                RecoverLockdownDose();
                if (File.Exists(DoseRecoveryPath)) return;
            }
            _doseEngineArmed = true;
            _doseEngineWasRunning = CoreEngine.IsRunning;
            _doseEngineRequested = false;
            _doseEngineStarted = false;
            _doseEngineIdle = _doseIdle = _doseRound = 0;
            _doseSnapshot.Clear();
            foreach (var key in DoseWall.Where(IsWallFeatureOn)) _doseSnapshot.Add(key);
            _doseFlipped.Clear();
            _doseWasEmpty = !HasLockdownDose();
            DeleteDoseRecovery();
            PanicAftermath.ReleaseEngineHold();
            _doseEngineKickoff?.Stop();
            _doseEngineKickoff = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _doseEngineKickoff.Tick += (_, _) =>
            {
                _doseEngineKickoff?.Stop();
                EnforceLockdownDoseEngine(atActivation: true);
            };
            _doseEngineKickoff.Start();
        });
        void Tick(TimeSpan _) => OnUi(() => EnforceLockdownDoseEngine(atActivation: false));
        void Deactivate() => OnUi(() =>
        {
            _doseEngineKickoff?.Stop();
            if (!_doseEngineArmed) return;
            _doseEngineArmed = false;
            foreach (var key in _doseFlipped.Where(key => !_doseSnapshot.Contains(key)).ToArray())
                SetWallFeature(key, false);
            // SetWallFeature saves on a debounce. Keep the journal until the restored flags reach disk.
            if (CoreSettings.Service?.TrySaveImmediate() != false)
            {
                foreach (var key in _doseFlipped.ToArray())
                    if (_doseSnapshot.Contains(key) || !IsWallFeatureOn(key)) _doseFlipped.Remove(key);
                WriteDoseRecovery();
            }
            else Log.Warning("Lockdown dose: settings could not be saved; recovery retained");
            // Stop also invalidates a start waiting for the Wayland portal (StartStop.cs).
            if (!_doseEngineWasRunning && !IsSessionFeatureLockActive
                && (_doseEngineStarted || (_doseEngineRequested && !CoreEngine.IsRunning && _engineGen == _doseEngineGeneration)))
                StopEngine();
        });
        ld.LockdownActivated += Activate;
        ld.CountdownTick += Tick;
        ld.LockdownDeactivated += Deactivate;
        Closed += (_, _) =>
        {
            _doseEngineKickoff?.Stop();
            ld.LockdownActivated -= Activate;
            ld.CountdownTick -= Tick;
            ld.LockdownDeactivated -= Deactivate;
        };
    }

    // WPF DoseKeeper:81-90,107-116: all wall flags and the off-wall census count.
    private bool HasLockdownDose()
    {
        var s = CoreSettings.Current;
        return DoseStarter.Any(key => IsWallFeatureOn(key) && DoseFeatureAvailable(key))
            || (s.MandatoryVideosEnabled && DoseFeatureAvailable("video")) || s.MindWipeEnabled
            || s.LockCardEnabled || s.BubbleCountEnabled || s.BrainDrainEnabled || s.AudioOnlySession
            || s.PopQuizEnabled || (s.CornerGifOverlays?.Exists(o => o != null && o.Enabled) == true)
            || (Autonomy.IsEnabled && s.AutonomyModeEnabled && s.AutonomyConsentGiven);
    }

    // A pending user START owns the portal request until it runs or Stop invalidates it.
    private bool CanStartLockdownDoseEngine() => _doseEngineArmed && LockdownActive
        && _pendingUserEngineGen != _engineGen
        && CoreSettings.Current.LockdownDoseKeeperEnabled && !IsSessionFeatureLockActive
        && !PanicAftermath.HoldsEngineRestart && HasLockdownDose();

    internal void EnforceLockdownDoseEngine(bool atActivation)
    {
        if (!_doseEngineArmed || !LockdownActive || !CoreSettings.Current.LockdownDoseKeeperEnabled) return;
        if (IsSessionFeatureLockActive) { _doseEngineIdle = _doseIdle = 0; return; }
        var empty = !HasLockdownDose();
        if (empty && !_doseWasEmpty && !atActivation)
            LockdownService.Current?.NotifyEscapeAttempt(EscapeKinds.Starve);
        _doseWasEmpty = empty;

        if (!CoreEngine.IsRunning)
        {
            if (PanicAftermath.HoldsEngineRestart || _pendingUserEngineGen == _engineGen) { _doseEngineIdle = 0; return; }
            if (!atActivation && ++_doseEngineIdle < 4) return;
            if (empty) ConscriptLockdownDose();
            _doseEngineIdle = _doseIdle = 0;
            if (!CanStartLockdownDoseEngine()) return;
            _doseEngineRequested = true;
            StartEngine(systemInitiated: true);
            _doseEngineGeneration = _engineGen;
            return;
        }
        _doseEngineIdle = 0;
        if (!empty) { _doseIdle = 0; return; }
        if (!atActivation && ++_doseIdle < Math.Max(2, 6 - 2 * _doseRound)) return;
        ConscriptLockdownDose();
        _doseIdle = 0;
    }

    private void ConscriptLockdownDose()
    {
        var round = _doseRound + 1;
        var starter = DoseStarter.Where(DoseFeatureAvailable).ToArray();
        var escalation = DoseFeatureAvailable("video") ? new[] { "video" } : Array.Empty<string>();
        var allowed = starter.Concat(escalation).ToArray();
        var picks = new List<string>();
        void Take(IEnumerable<string> keys)
        {
            var shuffled = keys.ToList();
            for (var i = shuffled.Count - 1; i > 0; i--)
            {
                var j = Random.Shared.Next(i + 1);
                (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
            }
            foreach (var key in shuffled)
            {
                if (picks.Count >= Math.Clamp(1 + round, 2, 4)) return;
                if (allowed.Contains(key) && !IsWallFeatureOn(key) && !picks.Contains(key)) picks.Add(key);
            }
        }
        Take(_doseSnapshot);
        Take(starter);
        if (round >= 2 || starter.Length == 0) Take(escalation);
        if (picks.Count == 0) { Log.Warning("Lockdown dose: no available feature to pick"); return; }
        _doseRound = round;
        foreach (var key in picks)
        {
            _doseFlipped.Add(key);
            WriteDoseRecovery();
            SetWallFeature(key, true);
        }
        // WPF DoseKeeper:383-385 and :412-415: the edge pulse and bark are not wired here yet.
    }

    private bool DoseFeatureAvailable(string key)
    {
        if (key == "video") return (DoseVideoReadyForTests ?? (LibVlcAudio.Shared != null)) && CoreEngine.Video?.HasPlayableContent() == true;
        if (DoseOverlaySupportForTests is bool supported) return supported;
        return key switch
        {
            "spiral" => SpiralOverlay.CanShowSpiral(this),
            "pinkfilter" => PinkFilterOverlay.CanShowTint(this),
            _ => X11Overlay.BackendOf(TryGetPlatformHandle()) != OverlayBackend.None && X11Overlay.IsAvailable,
        };
    }

    private void WriteDoseRecovery()
    {
        try
        {
            var flipped = _doseFlipped.Where(k => !_doseSnapshot.Contains(k)).ToArray();
            if (flipped.Length == 0) { DeleteDoseRecovery(); return; }
            Directory.CreateDirectory(CorePaths.UserData);
            File.WriteAllText(DoseRecoveryPath, JsonConvert.SerializeObject(new { Flipped = flipped }));
        }
        catch (Exception ex) { Log.Warning(ex, "Lockdown dose recovery write failed"); }
    }

    private static void DeleteDoseRecovery()
    {
        try { if (File.Exists(DoseRecoveryPath)) File.Delete(DoseRecoveryPath); }
        catch (Exception ex) { Log.Warning(ex, "Lockdown dose recovery delete failed"); }
    }

    // WPF DoseKeeper:545-575, called after settings load and before the engine can start.
    internal static void RecoverLockdownDose()
    {
        var delete = false;
        try
        {
            if (!File.Exists(DoseRecoveryPath)) return;
            var state = JsonConvert.DeserializeObject<Dictionary<string, string[]>>(File.ReadAllText(DoseRecoveryPath));
            if (state == null || !state.TryGetValue("Flipped", out var flipped)) return;
            var s = CoreSettings.Current;
            foreach (var key in flipped)
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
            delete = CoreSettings.Service?.TrySaveImmediate() != false;
            if (!delete) Log.Warning("Lockdown dose recovery: settings could not be saved; recovery retained");
        }
        catch (Exception ex) { Log.Warning(ex, "Lockdown dose recovery failed"); }
        finally { if (delete) DeleteDoseRecovery(); }
    }
}
