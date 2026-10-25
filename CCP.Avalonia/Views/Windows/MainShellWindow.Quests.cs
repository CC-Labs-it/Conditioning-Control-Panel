using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private readonly EventBurstLayer _questCompletionBurst = new();

        /// <summary>WPF MainWindow.EventFx.cs CelebrateQuestComplete: the nav row is the last
        /// anchor when the quest's own bar is off screen or unmeasured. NavAnchorForTab maps
        /// Quests to its owning section (You). Like WPF FireBurstAtFirstVisible (EventFx.cs:224-228),
        /// only the primary anchor gets the authored RightEdge spot; a fallback bursts from its centre.
        /// EventBurstLayer owns the motion, active-window and visible-anchor gates.</summary>
        internal void CelebrateQuestNav()
        {
            if (NavAnchorForTab("quests") is { } anchor)
                _questCompletionBurst.Fire(anchor, QuestsTabView.QuestBurstCount);
        }

        // Header stamps live in MainShellWindow.QuestStamps.cs. Voice/banner/popup/sync
        // live in QuestsTabView.Quests.cs, subscribed even when another tab is selected.
    }
}
