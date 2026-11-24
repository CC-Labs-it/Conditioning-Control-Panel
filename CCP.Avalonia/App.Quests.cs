using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia;

public partial class App
{
    // WPF Services/Progression/AchievementService.cs:750 and SkillTreeService.cs:461-527.
    internal static void WireQuestRewards(SessionRunner? runner)
    {
        CoreQuests.CheckPerfectWeekBonusProvider = () =>
        {
            if (CoreSettings.Service?.Current is not { } settings) return 0;
            // WPF SkillTreeService.cs:519 tolerates an unavailable progression service.
            // A failed bonus must leave the latch unpaid and let CompleteQuest finish its pass.
            if (CoreProgression.AddXPProvider is not { } grant) return 0;
            int xp;
            try
            {
                xp = SkillTreeRules.ClaimPerfectWeekBonus(settings,
                    amount => grant(amount, "Other"), () => CoreSettings.Save());
            }
            catch (System.Exception ex)
            {
                Serilog.Log.Warning(ex, "Perfect week bonus grant failed");
                return 0;
            }
            if (xp > 0)
            {
                var streak = settings.DailyQuestStreak;
                var generation = _perfectWeekGeneration;
                Dispatcher.UIThread.Post(() =>
                {
                    try { if (generation == _perfectWeekGeneration) ShowPerfectWeekBonus(xp, streak); }
                    catch (System.Exception ex) { Serilog.Log.Warning(ex, "Perfect week notification failed"); }
                });
            }
            return xp;
        };
        if (runner != null) runner.Stopped += (_, completed) =>
        {
            if (completed) Quests?.TrackSessionCompleted();
        };
    }

    // WPF Services/Progression/SkillTreeService.cs:535-563 creates one popup per bonus.
    // Register these new surfaces for panic as well.
    private static int _perfectWeekGeneration;
    private static readonly System.Collections.Generic.List<Views.Windows.AchievementPopup> _perfectWeekPopups = new();

    internal static Views.Windows.AchievementPopup ShowPerfectWeekBonus(int xp, int streak)
    {
        var popup = new Views.Windows.AchievementPopup(
            Loc.Get("skill_perfect_week_name_popup"),
            Loc.GetF("skill_perfect_week_flavor_popup", streak, xp),
            "skills/perfect_bimbo_week.png");
        _perfectWeekPopups.Add(popup);
        popup.Closed += (_, _) => _perfectWeekPopups.Remove(popup);
        popup.Show();
        return popup;
    }

    // WPF SkillTreeService.cs:420-445: show the daily login award once the bonus is paid.
    internal static void QueueDailyStreakBonus(int xp, int streak)
    {
        var generation = _perfectWeekGeneration;
        Dispatcher.UIThread.Post(() =>
        {
            if (generation != _perfectWeekGeneration) return;
            try
            {
                var popup = new Views.Windows.AchievementPopup(
                    Loc.GetF("skill_streak_bonus_name", streak),
                    Loc.GetF("skill_streak_bonus_flavor", xp, streak),
                    "skills/milestone_rewards.png", headerText: Loc.Get("skill_daily_streak_bonus_header"));
                popup.FindControl<Controls.IconGlyph>("TxtHeaderIcon")!.Apply(Controls.IconMap.ForSkill("milestone_rewards")!.Value);
                _perfectWeekPopups.Add(popup);
                popup.Closed += (_, _) => _perfectWeekPopups.Remove(popup);
                popup.Show();
            }
            catch (System.Exception ex) { Serilog.Log.Warning(ex, "Daily streak notification failed"); }
        }, DispatcherPriority.ApplicationIdle);
    }

    internal static void StopPerfectWeekBonus()
    {
        _perfectWeekGeneration++; // A queued notification must not appear after panic.
        foreach (var popup in _perfectWeekPopups.ToArray()) popup.Close();
        _perfectWeekPopups.Clear();
    }

}
