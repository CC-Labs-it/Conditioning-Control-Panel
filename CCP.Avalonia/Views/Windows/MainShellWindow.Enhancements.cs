// PORTED-IN-PART from ConditioningControlPanel/MainWindow/MainWindow.Enhancements.cs (2318 lines).
//
// The drawing half lives on the tab itself (Views/Tabs/EnhancementsTabView.axaml.cs), because
// nothing in it needs the window: RefreshEnhancementsUI, DrawSkillTree, CreateSkillTreeHeader
// (title, capstone chip, active bonuses, points, prestige, Ditzy Data stats toggle, XP multiplier,
// conditioning time), CreateAnimatedSkillTreeBrush, DrawConnectionLines, CreateSkillNode,
// CreateSkillPlaceholderGradient, PopulateSecretSkills, CreateHiddenSecretCard,
// CreateSecretSkillCard and RefreshActiveBonuses. The multiplier/breakdown/time math is Core
// SkillTreeRules (WPF SkillTreeService delegates to it). OnPinkRushStarted/Ended: PinkRushHost.
// PRO panels + season/sparkline/heatmap charts live on EnhancementsTabView.Analytics.cs;
// aggregation is Core EnhancementAnalytics, also called by WPF.
//
// SkillCard_Click + the purchase/prestige bursts: EnhancementsTabView.Purchase.cs over Core SkillPurchase;
// UpdateTrophyCaseColumns: MainShellWindow.Leaderboard.cs. OnLuckyProc/EnsureLuckyToast/PlaceLuckyToast/
// CloseLuckyToast: LuckyToast.cs, raised by FlashOverlay's roll.
// CelebratePrestige's full-window sheen: EnhancementsTabView.Purchase.cs SweepPrestigeSheen.
// ponytail: still missing - the 401 token recovery + retry before a purchase fails, and the lucky-bubble
//   proc (no bubble roll on this head).

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // No member of this partial is referenced from MainShellWindow.axaml.
    }
}
