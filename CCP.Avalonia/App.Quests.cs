using System;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia;

public partial class App
{
    /// <summary>WPF AchievementService.TrackSessionComplete and App.CommandReceived subscriptions.
    /// App-lifetime producers: credit also arrives while the Quests tab is hidden.</summary>
    internal static void WireQuestProgress(SessionRunner runner, RemoteRelay relay, int loginStreak)
    {
        // ponytail: WPF App.xaml.cs:3412 feed only; the login lifecycle remains head-only.
        Quests?.TrackStreak(loginStreak);
        runner.Stopped += (_, completed) =>
        {
            if (!completed) return;
            try { Quests?.TrackSessionCompleted(); }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Session quest credit failed"); }
        };
        // Raised only after the relay's gate and execution succeed, including quiet verbs.
        // Differs from WPF (RemoteControlService.cs:657-659), which credits refused commands too.
        relay.CommandReceived += (_, _) =>
        {
            try { Quests?.TrackRemoteCommand(); }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Remote quest credit failed"); }
        };
    }

    internal static AchievementPopup? PerfectWeekPopup { get; private set; }
    private static int _perfectWeekGeneration;

    /// <summary>WPF App.xaml.cs seeds SkillTree.CheckPerfectWeekBonus before the quest board starts.</summary>
    internal static void SeedPerfectWeekBonus()
    {
        CoreQuests.CheckPerfectWeekBonusProvider = () =>
        {
            if (CoreSettings.Service?.Current is not { } settings) return 0;
            int xp = SkillTreeRules.CheckPerfectWeekBonus(settings,
                amount => CoreProgression.AddXP(amount, "Other"), () => CoreSettings.Save());
            if (xp <= 0) return 0;
            int streak = settings.DailyQuestStreak;
            int generation = _perfectWeekGeneration;
            Dispatcher.UIThread.Post(() =>
            {
                if (generation != _perfectWeekGeneration) return; // panic also cancels queued presentation
                try
                {
                    PerfectWeekPopup?.Close();
                    var popup = PerfectWeekPopup = new AchievementPopup(Loc.Get("skill_perfect_week_name_popup"),
                        Loc.GetF("skill_perfect_week_flavor_popup", streak, xp), "skills/perfect_bimbo_week.png");
                    popup.Closed += (_, _) => { if (ReferenceEquals(PerfectWeekPopup, popup)) PerfectWeekPopup = null; };
                    popup.Show(); // same six-second AchievementPopup and copy as WPF SkillTreeService
                }
                catch (Exception ex) { Serilog.Log.Warning(ex, "Perfect Week notification failed"); }
            });
            return xp; // QuestService must not award it a second time
        };
    }

    internal static void ClosePerfectWeekPopup()
    {
        _perfectWeekGeneration++;
        var popup = PerfectWeekPopup;
        PerfectWeekPopup = null;
        popup?.Close();
    }
}
