// PORTED-IN-PART from ConditioningControlPanel/MainWindow/MainWindow.TabFxPresetsQuestsAchievements.cs.
// Per-item builders and selection moved into the tab views on this head, so their FX belong there:
// PresetsTabView.Fx.cs owns the selected preset/session sheen, row/chip hover lift, primary-button
// press squish, and independent preset/session/Takeaway entrance zones. CardSheenAdorner already
// uses the shared frame clock; focus/minimise/motion/performance/tab-hide/unload gates live in the view.
// QuestsTabView.Quests.cs now feeds its real weekly fraction into a 0.6 s width transition and
// seats resizes immediately. AchievementsTabView.Fx.cs already owns its unlocked tilt/lift/pop/stagger.
//
// Follow-up wire-shell-tab-fx-presets-quests-achievements-b: WPF's data-before-FX arrival funnel
// (SyncCustomSessionsFromDisk, RepaintSessionRack, RefreshTakeawayShelf) and IsIncomingTab guard
// for the shell's transient outgoing-tab re-show. The receipt drawer is still a separate stub;
// its data path and the cross-head custom-session discovery rule cannot be invented here.
namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow { }
}
