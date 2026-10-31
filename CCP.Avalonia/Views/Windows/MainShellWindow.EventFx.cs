// PORTED from WPF MainWindow.EventFx.cs: the event callers on this head. Achievement/quest
// tiles already own their on-tab burst; the shell owns the rail fallback, level cap and program
// banner. EventBurstLayer carries the shared particle/focus gates and native-pixel anchoring.
// WPF MainWindow.EventFx.cs:249,276,412. Quest nav fallback already lives in MainShellWindow.Quests.cs.
// Enhancement purchase/prestige: MainShellWindow.Enhancements.cs. THE BANK remains follow-up.

using System;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Models;
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
            EventHandler<Achievement> unlocked = (_, _) => Dispatcher.UIThread.Post(() =>
            {
                // Visible tiles own the attempt and its nav fallback (WPF EventFx.cs:295).
                if (Named<Tabs.AchievementsTabView>("AchievementsTab")?.IsEffectivelyVisible != true)
                    CelebrateAchievementNav();
            });
            if (achievements != null) achievements.Unlocked += unlocked;
            Closed += (_, _) =>
            {
                _eventFxClosed = true; // posted callbacks must not rebuild a detached layer
                if (achievements != null) achievements.Unlocked -= unlocked;
                StopEventFx();
            };
        }

        internal void CelebrateAchievementNav() =>
            FireEventBurst(NavAnchorForTab("achievements"), 95, color: Env.GlowColor);

        // WPF OnLevelUp calls this before UpdateLevelDisplay: keep the flash on the old fill,
        // while particles use the track's cap even when the fill resets to the next level.
        private void CelebrateLevelUp()
        {
            if (_eventFxClosed) return;
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

        // Stricter than WPF: panic stops even transient sparks (MainWindow.xaml.cs:1954).
        internal void StopEventFx()
        {
            _eventBursts.Stop();
            _questCompletionBurst.Stop();
            ParkPrestigeSheen();
        }
    }
}
