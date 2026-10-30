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
// Purchases: EnhancementsTabView.Purchase.cs over Core SkillPurchase (auth recovery + sync retry).
// Part B: node/prestige bursts and sheen, trophy columns, immediate Pink Rush, SkillUnlocked bark.
// Still missing: lucky-proc toast and season recording host.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // No member of this partial is referenced from MainShellWindow.axaml.
    }
}
