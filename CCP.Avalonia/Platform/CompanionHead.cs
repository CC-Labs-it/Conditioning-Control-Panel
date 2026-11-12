using System;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Companion;
using Serilog;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;

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
    private static CommunityPromptService? _prompts;

    internal static bool Started => _activeTimer != null;
    internal static IDisposable? DrainTimer => _drainTimer;

    internal static void Start()
    {
        if (Started) return;
        try { CompanionCore.Start(); } catch (Exception ex) { Log.Warning(ex, "CompanionCore start failed"); }
        CoreModsHooks.ActiveCompanionProvider = () => CompanionCore.ActiveCompanion;
        // WPF App.xaml.cs:2708-2713 and CompanionService.cs:439: mod switches warn too.
        var prompts = _prompts = new CommunityPromptService();
        prompts.ValidationAdvisory += (_, count) =>
        {
            var epoch = WorkshopCommunityCell.PanicEpoch;
            Dispatcher.UIThread.Post(async () =>
            {
                if (epoch == WorkshopCommunityCell.PanicEpoch && ReferenceEquals(_prompts, prompts))
                    await WorkshopCommunityCell.ShowPromptDialogAsync(MainShellWindow.Current,
                        new MessageDialog(Loc.Get("community_prompt_warning_title"), Loc.GetF("community_prompt_warning_body", count), false));
            });
        };
        // Resolve the live head per call: a captured hook keeps working after Stop/Start and no-ops while stopped.
        CoreModsHooks.SwitchCompanion = id =>
        {
            if (_prompts is { } live) CompanionCore.SwitchCompanion(id, live);
        };

        CompanionCore.Switched += OnSwitched;
        _activeTimer = DispatcherTimer.Run(() => { try { CompanionCore.UpdateActiveTime(); } catch { } return true; }, TimeSpan.FromMinutes(1));
        UpdateDrainTimer();
    }

    internal static bool SetPerk(CompanionBonusType perk)
    {
        if (!CompanionPerks.TrySelect(CoreSettings.Current, perk)) return false;
        CoreSettings.Save();
        UpdateDrainTimer();
        return true;
    }

    private static void OnSwitched(CompanionId _) => UpdateDrainTimer();

    private static void UpdateDrainTimer()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            var prompts = _prompts;
            Dispatcher.UIThread.Post(() =>
            {
                if (prompts != null && ReferenceEquals(_prompts, prompts)) UpdateDrainTimer();
            });
            return;
        }
        _drainTimer?.Dispose();
        _drainTimer = null;
        if (!Started || CompanionCore.ActivePerk != CompanionBonusType.XPDrain) return;
        _drainTimer = DispatcherTimer.Run(() => { try { CompanionCore.DrainTick(DateTime.UtcNow); } catch (Exception ex) { Log.Debug("Drain tick: {E}", ex.Message); } return true; },
            TimeSpan.FromSeconds(CompanionCore.DrainIntervalSeconds));
    }

    /// <summary>Stops both timers (tests; the hooks are the caller's to restore).</summary>
    internal static void Stop()
    {
        CompanionCore.Switched -= OnSwitched;
        _drainTimer?.Dispose();
        _activeTimer?.Dispose();
        _drainTimer = _activeTimer = null;
        _prompts?.Dispose();
        _prompts = null;
    }
}
