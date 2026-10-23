// PORTED into Views/Tabs/QuestsTabView.Quests.cs (the view owns every control it paints):
// RerollDailySlot, BtnRerollWeekly_Click, RefreshQuestUI, BuildDailyCardModel,
// ComputeQuestXpDisplay (XpCurve + SkillTreeRules, the maths CompleteQuest pays) and
// RefreshStreakCalendar, all off App.Quests (the Core QuestService).
//
// ponytail, still missing:
//   Quest art and mod/settings repaint now live in QuestsTabView.Art.cs.
//   RefreshPunchCard / BuildPunchHole - IntakePunchCardService is WPF-only.
//   BtnFixStreak_Click / StreakFixDay_Click / ExitStreakFixMode: ported in QuestsTabView.StreakFix.cs.
//   Remaining producer/account dependencies are listed in the shell-quests-tab row of docs/avalonia-parity.md.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
    }
}
