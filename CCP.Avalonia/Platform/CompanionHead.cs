using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Companion;
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
    private static IDisposable? _drainTimer, _activeTimer;
    internal static Window? AdvisoryOwnerForTests;

    internal static bool Started => _drainTimer != null;

    internal static void Start()
    {
        if (Started) return;
        try { CompanionCore.Start(); } catch (Exception ex) { Log.Warning(ex, "CompanionCore start failed"); }
        CoreModsHooks.ActiveCompanionProvider = () => CompanionCore.ActiveCompanion;
        CoreModsHooks.SwitchCompanion = id => CompanionCore.SwitchCompanion(id);
        CompanionCore.PromptValidationAdvisory += ShowPromptValidationAdvisory;

        // ponytail: WPF runs this timer only under Brain Parasite; here DrainTick returns at once for other perks.
        _drainTimer = DispatcherTimer.Run(() => { try { CompanionCore.DrainTick(DateTime.UtcNow); } catch (Exception ex) { Log.Debug("Drain tick: {E}", ex.Message); } return true; },
            TimeSpan.FromSeconds(CompanionCore.DrainIntervalSeconds));
        _activeTimer = DispatcherTimer.Run(() => { try { CompanionCore.UpdateActiveTime(); } catch { } return true; }, TimeSpan.FromMinutes(1));
    }

    // WPF App.xaml.cs:2708-2713 posts an ownerless warning, including while the shell is hidden.
    private static void ShowPromptValidationAdvisory(int count) => Dispatcher.UIThread.Post(async () =>
    {
        try
        {
            var owner = AdvisoryOwnerForTests ??
                (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            var dialog = new MessageDialog(Loc.Get("community_prompt_warning_title"),
                Loc.GetF("community_prompt_warning_body", count), showCancel: false);
            await dialog.ShowDialogSafe<bool?>(owner);
        }
        catch (Exception ex) { Log.Debug(ex, "Community prompt advisory failed"); }
    });

    /// <summary>Stops both timers (tests; the hooks are the caller's to restore).</summary>
    internal static void Stop()
    {
        CompanionCore.PromptValidationAdvisory -= ShowPromptValidationAdvisory;
        _drainTimer?.Dispose();
        _activeTimer?.Dispose();
        _drainTimer = _activeTimer = null;
    }
}
