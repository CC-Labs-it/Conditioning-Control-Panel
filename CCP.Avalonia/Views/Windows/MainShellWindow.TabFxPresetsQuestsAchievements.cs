// PORTED-IN-PART from ConditioningControlPanel/MainWindow/MainWindow.TabFxPresetsQuestsAchievements.cs.
// Per-item builders and selection moved into the tab views on this head, so their FX belong there:
// PresetsTabView.Fx.cs owns the selected preset/session sheen, row/chip hover lift, primary-button
// press squish, and independent preset/session/Takeaway entrance zones. CardSheenAdorner already
// uses the shared frame clock; focus/minimise/motion/performance/tab-hide/unload gates live in the view.
// QuestsTabView.Quests.cs now feeds its real weekly fraction into a 0.6 s width transition and
// seats resizes immediately. AchievementsTabView.Fx.cs already owns its unlocked tilt/lift/pop/stagger.
//
// The data-before-FX arrival funnel (SyncCustomSessionsFromDisk, RepaintSessionRack, shelf repaint)
// runs in PresetsTabView.Fx.cs OnPropertyChanged before the stagger. IsIncomingTab has nothing to
// filter: ShowTab snaps tabs, there is no FadeOutgoingTab re-show on this head yet.
// Follow-up wire-shell-tab-fx-presets-quests-achievements-b-next: real Takeaway receipts, blocked on
// JustDropOrdersService (not in Core; see MainShellWindow.Takeaway.cs).
namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow { }
}
