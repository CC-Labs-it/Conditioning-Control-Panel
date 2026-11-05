using System;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Companion;
using Serilog;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;

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
    private static IDisposable? _drainTimer, _activeTimer;
    private static CommunityPromptService? _prompts;

    internal static bool Started => _drainTimer != null;

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
        CoreModsHooks.SwitchCompanion = id =>
        {
            if (ReferenceEquals(_prompts, prompts)) CompanionCore.SwitchCompanion(id, prompts);
        };

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
        _prompts?.Dispose();
        _prompts = null;
    }
}
