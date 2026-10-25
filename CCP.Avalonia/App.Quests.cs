using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia;

public partial class App
{
    // WPF AchievementService.TrackSessionComplete and SkillTreeService.CheckPerfectWeekBonus.
    internal static void WireQuestRewards(SessionRunner? runner)
    {
        CoreQuests.CheckPerfectWeekBonusProvider = () =>
        {
            if (CoreSettings.Service?.Current is not { } settings) return 0;
            var xp = SkillTreeRules.ClaimPerfectWeekBonus(settings,
                amount => CoreProgression.AddXP(amount, "Other"), () => CoreSettings.Save());
            if (xp > 0)
            {
                var streak = settings.DailyQuestStreak;
                Dispatcher.UIThread.Post(() =>
                {
                    try { ShowPerfectWeekBonus(xp, streak); }
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

    // WPF SkillTreeService.cs:540-546 lets bonus popups stack; panic closes them all.
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
        foreach (var popup in _perfectWeekPopups.ToArray()) popup.Close();
        _perfectWeekPopups.Clear();
    }

}
