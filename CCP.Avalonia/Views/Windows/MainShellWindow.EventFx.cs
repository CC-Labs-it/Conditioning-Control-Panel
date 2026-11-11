// PORTED from WPF MainWindow.EventFx.cs: the event callers on this head. Achievement/quest
// tiles already own their on-tab burst; the shell owns the rail fallback and the level cap.
// The program day banner's 90-spark burst (WPF EventFx.cs:412, ProgramDayBurstCount) rides the
// Programs tab's own layer (ProgramsTabView.CompletionFx QueueProgramBurst), so it is not fired here.
// EventBurstLayer carries the shared particle/focus gates and native-pixel anchoring.
// WPF MainWindow.EventFx.cs:249,276. Quest nav fallback already lives in MainShellWindow.Quests.cs.
// THE BANK remains separate follow-up work.

using System;
using System.Linq;
using ConditioningControlPanel.Avalonia.Helpers;
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
        private BeatLoop? _prestigeSheenBeat;

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
            void MotionChanged() { if (!Env.AllowFeedbackParticles) ParkPrestigeSheen(); }
            Env.MotionGateChanged += MotionChanged;
            Deactivated += (_, _) => ParkPrestigeSheen();
            Closed += (_, _) =>
            {
                _eventFxClosed = true; // posted callbacks must not rebuild a detached layer
                Env.MotionGateChanged -= MotionChanged;
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

        private bool FireEventBurst(Control? anchor, int count, bool rightEdge = false, Color? color = null)
        {
            if (_eventFxClosed || anchor == null) return false;
            try { return _eventBursts.Fire(anchor, count, rightEdge, color); }
            catch (Exception ex) { Serilog.Log.Debug("Event burst: {Error}", ex.Message); return false; }
        }

        // WPF EventFx.cs:428-455. Called before the tab rebuild detaches either anchor.
        internal void CelebrateEnhancementPurchase(Control node, Control? prestigeRow, long rankBefore)
        {
            var fallback = NavAnchorForTab("enhancements");
            if (!FireEventBurst(node, 100)) FireEventBurst(fallback, 100);
            var rankNow = 1 + (App.Achievements?.Progress?.LifetimeSkillPointsSpent ?? 0) / 100;
            if (rankNow <= rankBefore || _eventFxClosed || !Env.AllowFeedbackParticles || !IsVisible || !IsActive || WindowState == WindowState.Minimized) return;
            if (!FireEventBurst(prestigeRow, 150, color: Env.GlowColor)) FireEventBurst(fallback, 150, color: Env.GlowColor);
            SweepPrestigeSheen();
        }

        private void SweepPrestigeSheen()
        {
            var host = Named<Grid>("EventFxHost");
            var band = Named<Border>("PrestigeSheen");
            var slide = (band?.RenderTransform as TransformGroup)?.Children.OfType<TranslateTransform>().FirstOrDefault();
            if (host == null || band == null || slide == null || host.Bounds.Width <= 0) return;
            ParkPrestigeSheen();
            _prestigeSheenBeat = new BeatLoop(band, seconds =>
            {
                if (!Env.AllowFeedbackParticles || !IsActive || !IsVisible || WindowState == WindowState.Minimized) { ParkPrestigeSheen(); return; }
                double progress = Math.Clamp(seconds / 1.15, 0, 1);
                SheenAt(host.Bounds.Width, band, slide, progress, 0.16);
                if (progress >= 1) ParkPrestigeSheen();
            }) { OnParked = ParkPrestigeSheen };
            _prestigeSheenBeat.Start();
        }

        private void ParkPrestigeSheen()
        {
            _prestigeSheenBeat?.Stop();
            _prestigeSheenBeat = null;
            if (Named<Border>("PrestigeSheen") is { } band) band.Opacity = 0;
        }

        // Stricter than WPF: panic stops even transient sparks (MainWindow.xaml.cs:1954).
        internal void StopEventFx()
        {
            ParkPrestigeSheen();
            _eventBursts.Stop();
            _questCompletionBurst.Stop();
        }
    }
}
