// PORTED into Views/Tabs/QuestsTabView.Quests.cs (the view owns every control it paints):
// RerollDailySlot, BtnRerollWeekly_Click, RefreshQuestUI, BuildDailyCardModel,
// ComputeQuestXpDisplay (XpCurve + SkillTreeRules, the maths CompleteQuest pays) and
// RefreshStreakCalendar, all off App.Quests (the Core QuestService); GetQuestArt into
// QuestsTabView.Art.cs; BtnFixStreak_Click / StreakFixDay_Click / ExitStreakFixMode /
// OnSettingsPropertyChangedForQuests into QuestsTabView.StreakFix.cs (rules: Core StreakFix).
//
// Not ported: RefreshPunchCard / BuildPunchHole - WPF hides the card while
// IntakePunchCardService.UiEnabled is false, so the panel stays collapsed here too.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
    }
}
