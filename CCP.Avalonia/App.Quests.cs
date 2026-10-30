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
            var xp = SkillTreeRules.ClaimPerfectWeekBonus(settings,
                amount => (CoreProgression.AddXPProvider ?? throw new System.InvalidOperationException("XP service unavailable"))(amount, "Other"),
                () => CoreSettings.Save());
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

    internal static void StopPerfectWeekBonus()
    {
        _perfectWeekGeneration++; // A queued notification must not appear after panic.
        foreach (var popup in _perfectWeekPopups.ToArray()) popup.Close();
        _perfectWeekPopups.Clear();
    }

}
