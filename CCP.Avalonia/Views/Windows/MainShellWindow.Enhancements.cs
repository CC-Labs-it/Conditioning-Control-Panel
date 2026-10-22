// PORTED-IN-PART from ConditioningControlPanel/MainWindow/MainWindow.Enhancements.cs (2318 lines).
//
// The drawing half lives on the tab itself (Views/Tabs/EnhancementsTabView.axaml.cs), because
// nothing in it needs the window: RefreshEnhancementsUI, DrawSkillTree, CreateSkillTreeHeader
// (title, capstone chip, active bonuses, points, prestige, Ditzy Data stats toggle, XP multiplier,
// conditioning time), CreateAnimatedSkillTreeBrush, DrawConnectionLines, CreateSkillNode,
// CreateSkillPlaceholderGradient, PopulateSecretSkills, CreateHiddenSecretCard,
// CreateSecretSkillCard and RefreshActiveBonuses. The multiplier/breakdown/time math is Core
// SkillTreeRules (WPF SkillTreeService delegates to it). OnPinkRushStarted/Ended: PinkRushHost.
//
// SkillCard_Click + CelebrateEnhancementPurchase + the prestige burst: EnhancementsTabView.Purchase.cs
// over Core SkillPurchase. The PRO panels: EnhancementsTabView.Analytics.cs.
//
// ponytail: still missing, each a whole feature:
//   The 401 token recovery before a purchase retry (WPF HandleUnauthorizedAsync), UpdateTrophyCaseColumns
//     (LeaderboardTabView forces the columns on) and the prestige window sheen (SweepSheen).
//   OnLuckyProc / EnsureLuckyToast / PlaceLuckyToast / CloseLuckyToast - nothing raises a lucky
//     proc on this head (RollLuckyFlash/RollLuckyBubble are SkillTreeService, head-side in WPF).

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // No member of this partial is referenced from MainShellWindow.axaml.
    }
}
