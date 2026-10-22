// PORTED from WPF MainWindow.EventFx.cs: the event callers on this head. Achievement/quest
// tiles already own their on-tab burst; the shell owns the rail fallback, level cap and program
// banner. EventBurstLayer carries the shared particle/focus gates and native-pixel anchoring.
// ponytail: enhancement purchase/prestige have no purchase-success path on this head yet;
// attach those two moments there when purchasing exists, rather than inventing an event.

using System;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private readonly EventBurstLayer _eventBursts = new();
        private bool _eventFxClosed;

        private void InitializeEventFx()
        {
            var achievements = App.Achievements;
            var quests = App.Quests;
            EventHandler<Achievement> unlocked = (_, _) => Dispatcher.UIThread.Post(() =>
            {
                // WPF CelebrateAchievementUnlock: a visible tile already celebrates itself.
                if (Named<Tabs.AchievementsTabView>("AchievementsTab")?.IsEffectivelyVisible != true)
                    FireEventBurst(NavAnchorForTab("achievements"), 95, color: Env.GlowColor);
            });
            EventHandler<QuestCompletedEventArgs> completed = (_, _) => Dispatcher.UIThread.Post(() =>
            {
                if (Named<Tabs.QuestsTabView>("QuestsTab")?.IsEffectivelyVisible != true)
                    FireEventBurst(NavAnchorForTab("quests"), 85);
            });
            if (achievements != null) achievements.Unlocked += unlocked;
            if (quests != null) quests.QuestCompleted += completed;
            Closed += (_, _) =>
            {
                _eventFxClosed = true; // posted callbacks must not rebuild a detached layer
                if (achievements != null) achievements.Unlocked -= unlocked;
                if (quests != null) quests.QuestCompleted -= completed;
                StopEventFx();
            };
        }

        // WPF OnLevelUp calls this before UpdateLevelDisplay: keep the flash on the old fill,
        // while particles use the track's cap even when the fill resets to the next level.
        private void CelebrateLevelUp()
        {
            if (_eventFxClosed) return;
            FlashLevelUp();
            FireEventBurst(Named<Border>("XPBarTrack"), 110, rightEdge: true);
        }

        internal void CelebrateProgramDayComplete(Control banner)
        {
            // It was hidden until this refresh; measure its new banner before anchoring sparks,
            // just as the quest tab lays out its freshly resized fills before celebrating.
            banner.UpdateLayout();
            FireEventBurst(banner, 90);
        }

        private void FireEventBurst(Control? anchor, int count, bool rightEdge = false, Color? color = null)
        {
            if (_eventFxClosed || anchor == null) return;
            try { _eventBursts.Fire(anchor, count, rightEdge, color); }
            catch (Exception ex) { Serilog.Log.Debug("Event burst: {Error}", ex.Message); }
        }

        internal void StopEventFx() => _eventBursts.Stop();
    }
}
