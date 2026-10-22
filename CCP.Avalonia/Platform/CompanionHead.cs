using System;
using Avalonia.Threading;
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

    internal static bool Started => _drainTimer != null;

    internal static void Start()
    {
        if (Started) return;
        try { CompanionCore.Start(); } catch (Exception ex) { Log.Warning(ex, "CompanionCore start failed"); }
        CoreModsHooks.ActiveCompanionProvider = () => CompanionCore.ActiveCompanion;
        CoreModsHooks.SwitchCompanion = id => CompanionCore.SwitchCompanion(id);

        // WPF App.xaml.cs: App.CommunityPrompts is created here and its advisory shown as a warning box.
        try
        {
            CompanionCore.Prompts.ValidationAdvisory += (_, count) => Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    if ((global::Avalonia.Application.Current?.ApplicationLifetime as global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow is { IsVisible: true } owner)
                        await Views.Dialogs.MessageDialog.ShowAsync(owner, Loc.Get("community_prompt_warning_title"), Loc.GetF("community_prompt_warning_body", count));
                }
                catch (Exception ex) { Log.Debug("Community prompt advisory failed: {E}", ex.Message); }
            });
        }
        catch (Exception ex) { Log.Warning(ex, "Community prompt service start failed"); }

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
