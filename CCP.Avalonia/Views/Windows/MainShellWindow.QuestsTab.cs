// PORTED into Views/Tabs/QuestsTabView.Quests.cs (the view owns every control it paints):
// RerollDailySlot, BtnRerollWeekly_Click, RefreshQuestUI, BuildDailyCardModel,
// ComputeQuestXpDisplay (XpCurve + SkillTreeRules, the maths CompleteQuest pays) and
// RefreshStreakCalendar, all off App.Quests (the Core QuestService).
//
// ponytail, still missing:
//   Quest art and mod/settings repaint now live in QuestsTabView.Art.cs.
//   RefreshPunchCard / BuildPunchHole - IntakePunchCardService is WPF-only.
//   BtnFixStreak_Click / StreakFixDay_Click / ExitStreakFixMode - SpendStreakFix, a server round trip.
//   wire-shell-quests-tab-b owns the remaining service/spend work above.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
    }
}
