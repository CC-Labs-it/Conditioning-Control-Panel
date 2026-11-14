// Engine-only part of WPF Services/Haptics/LockdownDoseKeeper.cs:217-260,272-335,397-409,448-509.
// The census is Core LockdownDoseRules (shared with WPF); empty-dose selection (conscription) and its
// recovery journal are not on this head yet (Core LockdownDoseRecovery only reads an old journal at launch).
using System;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Haptics;
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
            // WPF DoseKeeper:246-247: switching Lockdown on is the user's own act, so an old panic no longer holds the engine.
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
                && _pendingUserEngineGen != _engineGen
                && ((_doseEngineStarted && CoreEngine.IsRunning) || (_doseEngineRequested && !CoreEngine.IsRunning && _engineGen == _doseEngineGeneration)))
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

    // WPF DoseKeeper:81-90,107-116 through the shared Core census (wall flags + the off-wall dose).
    private bool HasLockdownDose() => !LockdownDoseRules.DoseIsEmpty(CoreSettings.Current, Autonomy.IsEnabled);

    // A pending user engine or session start owns the portal request until it runs or Stop invalidates it.
    private bool CanStartLockdownDoseEngine() => _doseEngineArmed && LockdownActive
        && _pendingUserEngineGen != _engineGen
        && CoreSettings.Current.LockdownDoseKeeperEnabled && !IsSessionFeatureLockActive
        && !PanicAftermath.HoldsEngineRestart && HasLockdownDose();

    internal void EnforceLockdownDoseEngine(bool atActivation)
    {
        if (!_doseEngineArmed || !LockdownActive || !CoreSettings.Current.LockdownDoseKeeperEnabled) return;
        if (CoreEngine.IsRunning || !CanStartLockdownDoseEngine()) { _doseEngineIdle = 0; return; }
        // WPF DoseKeeper:141-142,322-335: a stopped engine gets EngineGraceSeconds ticks before the restart.
        if (!atActivation && ++_doseEngineIdle < LockdownDoseRules.EngineGraceSeconds) return;
        _doseEngineIdle = 0;
        _doseEngineRequested = true;
        StartEngine(systemInitiated: true, lockdownDose: true);
        _doseEngineGeneration = _engineGen;
    }
}
