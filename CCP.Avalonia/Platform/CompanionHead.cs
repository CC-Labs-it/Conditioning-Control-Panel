using System;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// The head half of WPF 7.1.5 <c>CompanionService</c> over Core <see cref="CompanionCore"/>: the drain and
/// active-time timers its constructor started, and the CoreModsHooks companion seams the mod service calls.
/// ponytail: the reactions to <see cref="CompanionCore"/>'s LevelUp / Switched (MainWindow.Companion.cs:48 toast
/// and sound, GamificationBridge.cs:226 best_friends, AvatarTubeWindow.Reactions.cs:542/570 tube lines) are the
/// next unit (line cap), with the shell's card / XP-bar refresh and drain flash.
/// </summary>
internal static class CompanionHead
{
    private static IDisposable? _activeTimer;
    private static DispatcherTimer? _drainTimer;

    internal static bool Started => _activeTimer != null;

    internal static void Start()
    {
        if (Started) return;
        try { CompanionCore.Start(); } catch (Exception ex) { Log.Warning(ex, "CompanionCore start failed"); }
        CoreModsHooks.ActiveCompanionProvider = () => CompanionCore.ActiveCompanion;
        CoreModsHooks.SwitchCompanion = id =>
        {
            if (id == CompanionCore.ActiveCompanion) return; // WPF's already-active switch is a no-op.
            if (CompanionCore.SwitchCompanion(id)) UpdateDrainTimer();
        };

        _activeTimer = DispatcherTimer.Run(() => { try { CompanionCore.UpdateActiveTime(); } catch { } return true; }, TimeSpan.FromMinutes(1));
        UpdateDrainTimer();
    }

    /// <summary>WPF SetPerk: persist an allowed choice, then restart its drain cadence.</summary>
    internal static bool SetPerk(CompanionBonusType perk)
    {
        if (!CompanionPerks.TrySelect(CoreSettings.Current, perk)) return false;
        CoreSettings.Save();
        UpdateDrainTimer();
        return true;
    }

    private static void UpdateDrainTimer()
    {
        _drainTimer?.Stop();
        _drainTimer = null;
        if (!Started || CompanionCore.ActivePerk != CompanionBonusType.XPDrain) return;
        _drainTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(CompanionCore.DrainIntervalSeconds) };
        _drainTimer.Tick += (_, _) =>
        {
            try { CompanionCore.DrainTick(DateTime.UtcNow); }
            catch (Exception ex) { Log.Debug("Drain tick: {E}", ex.Message); }
        };
        _drainTimer.Start();
    }

    /// <summary>Stops both timers (tests; the hooks are the caller's to restore).</summary>
    internal static void Stop()
    {
        _drainTimer?.Stop();
        _activeTimer?.Dispose();
        _drainTimer = null;
        _activeTimer = null;
    }
}
