using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private readonly EventBurstLayer _questCompletionBurst = new();

        /// <summary>WPF MainWindow.EventFx.cs CelebrateQuestComplete: the nav row is the last
        /// anchor when the quest's own bar is off screen or unmeasured. NavAnchorForTab maps
        /// Quests to its owning section (You); the authored burst spot is RightEdge, even here.
        /// EventBurstLayer owns the motion, active-window and visible-anchor gates.</summary>
        internal void CelebrateQuestNav()
        {
            if (NavAnchorForTab("quests") is { } anchor)
                _questCompletionBurst.Fire(anchor, QuestsTabView.QuestBurstCount, rightEdge: true);
        }

        // ponytail: flash voice lines still need the FlashService audio pool, and header stamps
        // remain MainShellWindow.QuestStamps.cs. Banner/popup/sync live in QuestsTabView.Quests.cs.
    }
}
