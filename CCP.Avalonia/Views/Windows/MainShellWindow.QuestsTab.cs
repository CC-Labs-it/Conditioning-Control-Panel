// PORTED into Views/Tabs/QuestsTabView.Quests.cs (the view owns every control it paints):
// RerollDailySlot, BtnRerollWeekly_Click, RefreshQuestUI, BuildDailyCardModel,
// ComputeQuestXpDisplay (XpCurve + SkillTreeRules, the maths CompleteQuest pays) and
// RefreshStreakCalendar, all off App.Quests (the Core QuestService).
//
// Quest art and mod/settings repaint now live in QuestsTabView.Art.cs.
// ponytail, still missing:
//   RefreshPunchCard / BuildPunchHole - IntakePunchCardService is WPF-only.
//   BtnFixStreak_Click / StreakFixDay_Click / ExitStreakFixMode: ported in QuestsTabView.StreakFix.cs.
//   App.Quests.cs wires session credit and the shared perfect-week milestone claim.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
    }
}
