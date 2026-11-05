using System;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Companion;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// The head half of WPF 7.1.5 <c>CompanionService</c> over Core <see cref="CompanionCore"/>: the drain and
/// active-time timers its constructor started, and the CoreModsHooks companion seams the mod service calls.
/// Shell refresh, milestone notifications/sound and drain flashes subscribe in MainShellWindow.Companion.cs.
/// ponytail: GamificationBridge.cs:226 best_friends and AvatarTubeWindow.Reactions.cs:542/570 tube lines remain separate reaction lanes.
/// </summary>
internal static class CompanionHead
{
    private static IDisposable? _drainTimer, _activeTimer;

    internal static bool Started => _drainTimer != null;

    internal static void Start()
    {
        if (Started) return;
        try { CompanionCore.Start(); } catch (Exception ex) { Log.Warning(ex, "CompanionCore start failed"); }
        CoreModsHooks.ActiveCompanionProvider = () => CompanionCore.ActiveCompanion;
        CoreModsHooks.SwitchCompanion = id => CompanionCore.SwitchCompanion(id);

        // ponytail: WPF runs this timer only under Brain Parasite; here DrainTick returns at once for other perks.
        _drainTimer = DispatcherTimer.Run(() => { try { CompanionCore.DrainTick(DateTime.UtcNow); } catch (Exception ex) { Log.Debug("Drain tick: {E}", ex.Message); } return true; },
            TimeSpan.FromSeconds(CompanionCore.DrainIntervalSeconds));
        _activeTimer = DispatcherTimer.Run(() => { try { CompanionCore.UpdateActiveTime(); } catch { } return true; }, TimeSpan.FromMinutes(1));
    }

    /// <summary>Stops both timers (tests; the hooks are the caller's to restore).</summary>
    internal static void Stop()
    {
        _drainTimer?.Dispose();
        _activeTimer?.Dispose();
        _drainTimer = _activeTimer = null;
    }
}
