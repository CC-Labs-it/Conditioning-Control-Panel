using System;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Haptics;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private void InitializeLockdownDose()
    {
        if (LockdownService.Current is not { } lockdown) return;
        var alive = true;
        var keeper = new CoreLockdownDoseKeeper(lockdown, LockdownDoseHostFor(() => alive ? this : null));
        CoreLockdownDoseKeeper.Current = keeper;
        CoreLockdownDoseKeeper.TakeoverRunningProvider = () => alive && Autonomy.IsEnabled;
        keeper.Install();
        Closed += (_, _) =>
        {
            alive = false;
            keeper.Dispose();
            if (ReferenceEquals(CoreLockdownDoseKeeper.Current, keeper))
            {
                CoreLockdownDoseKeeper.Current = null;
                CoreLockdownDoseKeeper.TakeoverRunningProvider = null;
            }
        };
    }

    internal static LockdownDoseHost LockdownDoseHostFor(Func<MainShellWindow?> shell) => new()
    {
        IsAlive = () => shell() != null,
        IsEngineRunning = () => CoreEngine.IsRunning,
        IsSessionFeatureLockActive = () => shell()?.IsSessionFeatureLockActive == true,
        StartEngine = () => (shell() ?? throw new InvalidOperationException("Shell unavailable")).StartEngine(systemInitiated: true),
        StopEngine = StopEngine,
        SetWallFeature = (key, on) => (shell() ?? throw new InvalidOperationException("Shell unavailable")).SetWallFeature(key, on),
        OnUi = action => { if (Dispatcher.UIThread.CheckAccess()) action(); else Dispatcher.UIThread.Post(action); },
        Bark = (features, round, started) => Platform.BarkHead.Engine?.Raise("LockdownConscript",
            c => c.Set("features", features).Set("round", (double)round).Set("engine", started ? 1.0 : 0.0)),
        // ponytail: Possession's edge pulse waits for the shell-possession lifecycle port.
    };
}
