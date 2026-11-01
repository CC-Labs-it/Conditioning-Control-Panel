// Engine-only part of WPF Services/Haptics/LockdownDoseKeeper.cs:217-260,272-335,397-409,448-509.
// Empty-dose selection, starvation and crash recovery still need the full keeper.
using System;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Safety;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private bool _doseEngineArmed, _doseEngineWasRunning, _doseEngineRequested, _doseEngineStarted;
    private int _doseEngineIdle, _doseEngineGeneration;
    private DispatcherTimer? _doseEngineKickoff;

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
            _doseEngineArmed = true;
            _doseEngineWasRunning = CoreEngine.IsRunning;
            _doseEngineRequested = false;
            _doseEngineStarted = false;
            _doseEngineIdle = 0;
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
        return s.FlashEnabled || s.SubliminalEnabled || s.SpiralEnabled || s.PinkFilterEnabled
            || s.BouncingTextEnabled || s.BubblesEnabled || s.MandatoryVideosEnabled || s.MindWipeEnabled
            || s.LockCardEnabled || s.BubbleCountEnabled || s.BrainDrainEnabled || s.AudioOnlySession
            || s.PopQuizEnabled || (s.CornerGifOverlays?.Exists(o => o != null && o.Enabled) == true)
            || (Autonomy.IsEnabled && s.AutonomyModeEnabled && s.AutonomyConsentGiven);
    }

    private bool CanStartLockdownDoseEngine() => _doseEngineArmed && LockdownActive
        && CoreSettings.Current.LockdownDoseKeeperEnabled && !IsSessionFeatureLockActive
        && !PanicAftermath.HoldsEngineRestart && HasLockdownDose();

    internal void EnforceLockdownDoseEngine(bool atActivation)
    {
        if (!_doseEngineArmed || !LockdownActive || !CoreSettings.Current.LockdownDoseKeeperEnabled) return;
        if (CoreEngine.IsRunning || !CanStartLockdownDoseEngine()) { _doseEngineIdle = 0; return; }
        if (!atActivation && ++_doseEngineIdle < 4) return; // WPF DoseKeeper:141-142,322-335
        _doseEngineIdle = 0;
        _doseEngineRequested = true;
        StartEngine(systemInitiated: true);
        _doseEngineGeneration = _engineGen;
    }
}
