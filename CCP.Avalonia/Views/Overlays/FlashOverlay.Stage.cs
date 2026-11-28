using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.SuperCore;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    /// <summary>
    /// Flashes on the shared Super stage (Core <see cref="SuperStageHost.Stage"/>), WPF FlashService.cs:2757-2840,
    /// :5317-5335 and FlashLayer.StepPool :1019: a card's break goes off as a blast (its ring knocks the
    /// cards in reach, its hole opens the fog), the bouncing word knocks the cards it crosses, and the
    /// Whirlpool drags every still flash in and draws it smaller until its eye takes it, which pays nothing.
    /// Every stage touch holds <see cref="SteamOverlay.Gate"/>, as the fog's step and draw do.
    /// </summary>
    internal static partial class FlashOverlay
    {
        private static bool _stageHooked, _poolHooked;
        private static DispatcherTimer? _poolTimer;
        private static long _lastPoolTick;

        /// <summary>The Whirlpool switch, read when a flash spawns (tests swap it).</summary>
        internal static Func<bool> PoolOn = () => SuperAccess.IsOn(SuperEffect.Vortex);

        /// <summary>Listen to the stage once (WPF HookStage :2761). Blasts are raised on the UI thread.</summary>
        internal static void HookDeckStage()
        {
            if (_stageHooked) return;
            _stageHooked = true;
            var stage = SuperStageHost.Stage;
            stage.Blasted += b => OnUi(() => DeckBlasted(b));
            stage.Swept += (l, t, r, b) => OnUi(() => DeckSwept(l, t, r, b));
        }

        private static void OnUi(Action a)
        {
            if (Dispatcher.UIThread.CheckAccess()) a();
            else Dispatcher.UIThread.Post(a);
        }

        /// <summary>WPF :5326-5333: the break on the stage. A card the pool shrank reaches less far; Break reach
        /// (DeckOptions) scales the ring, never the hole.</summary>
        internal static void DeckBreakBlast(FlashOverlayWindow w, PixelRect r)
        {
            double side = Math.Max(r.Width, r.Height), small = Math.Clamp(w.PoolScale, 0.35, 1);
            var reach = DeckOptions.ReachSides(CoreSettings.Current.DeckBreakReach) * side * small;
            try
            {
                lock (SteamOverlay.Gate)
                    SuperStageHost.Stage.Blast(new Blast(r.X + r.Width / 2.0, r.Y + r.Height / 2.0, reach, side * 0.8 * small, BlastKind.CardBreak));
            }
            catch (Exception ex) { Log.Debug("Flicker Deck: break blast failed: {E}", ex.Message); }
        }

        /// <summary>WPF OnStageBlast (:2784): every card inside the ring is knocked (one flip, as a click)
        /// when the ring gets there. The card that broke is off the active list already.</summary>
        private static void DeckBlasted(Blast b)
        {
            if (b.ReachPx <= 0) return;
            foreach (var (w, r) in Active.ToList())
            {
                if (w.Deck == null || w.PoolEaten) continue;
                double dx = r.X + r.Width / 2.0 - b.X, dy = r.Y + r.Height / 2.0 - b.Y, d = Math.Sqrt(dx * dx + dy * dy);
                if (d > b.ReachPx + Math.Max(r.Width, r.Height) * 0.3) continue;
                After(() => Knock(w), TimeSpan.FromMilliseconds(Math.Clamp(d / 1.4, 40, 900)));
            }
        }

        /// <summary>WPF OnStageSwept (:2797): a card the bouncing word crosses is knocked, at most once every 1.6 s.</summary>
        private static void DeckSwept(double left, double top, double right, double bottom)
        {
            var now = Clock.GetTimestamp();
            foreach (var (w, r) in Active.ToList())
            {
                if (w.Deck == null || w.PoolEaten) continue;
                double cx = r.X + r.Width / 2.0, cy = r.Y + r.Height / 2.0;
                if (cx < left - r.Width * 0.25 || cx > right + r.Width * 0.25 || cy < top - r.Height * 0.25 || cy > bottom + r.Height * 0.25) continue;
                if (w.LastKnockTick != 0 && Clock.GetElapsedTime(w.LastKnockTick, now).TotalSeconds < 1.6) continue;
                w.LastKnockTick = now;
                DeckPress(w);
            }
        }

        /// <summary>WPF KnockLater (:2812): the ring arrived; a card still up takes one press.</summary>
        private static void Knock(FlashOverlayWindow w)
        {
            if (w.Deck == null || w.PoolEaten || !Active.Any(e => e.Window == w)) return;
            w.LastKnockTick = Clock.GetTimestamp();
            DeckPress(w);
        }

        // ---- the Whirlpool's pull (WPF FlashLayer.StepPool :1019, FlashService.PoolAte :2829) ----

        /// <summary>Start the pull tick when a flash is up and the Whirlpool is on (a spawn, or the switch).</summary>
        internal static void EnsurePoolTick()
        {
            if (!_poolHooked)
            {
                _poolHooked = true;
                SuperAccess.Changed += e => { if (e == SuperEffect.Vortex) OnUi(EnsurePoolTick); };
            }
            if (_poolTimer != null || Active.Count == 0 || !PoolOn()) return;
            _lastPoolTick = Clock.GetTimestamp();
            _poolTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) => PoolTick());
            _poolTimer.Start();
        }

        internal static bool PoolTickRunning => _poolTimer != null;

        private static void StopPoolTick() { _poolTimer?.Stop(); _poolTimer = null; }

        /// <summary>One frame of the pull for every still flash; a flash in motion (drift, swing, a drag) keeps
        /// its own path. Stops itself when no flash is up, or when the switch is off and every flash is home.</summary>
        internal static void PoolTick()
        {
            var now = Clock.GetTimestamp();
            var dt = Math.Min(0.1, Clock.GetElapsedTime(_lastPoolTick, now).TotalSeconds);
            _lastPoolTick = now;
            var stage = SuperStageHost.Stage;
            var moved = new List<int>();
            var displaced = false;
            lock (SteamOverlay.Gate)
            {
                for (var i = 0; i < Active.Count; i++)
                {
                    var (w, r) = Active[i];
                    if (w.Exit != null || w.IsFadingOut || w.Shatter != null || w.DeckShatter != null || w.PoolEaten
                        || Drifting.Any(e => e.Window == w)) continue;
                    var pull = stage.PoolLive ? stage.Pull(r.X + r.Width / 2.0, r.Y + r.Height / 2.0, 1.4, PullKind.Picture) : default;
                    if (!pull.Inside && w.PoolVx == 0 && w.PoolVy == 0 && w.PoolScale >= 0.999) { w.PoolScale = 1; continue; }
                    if (w.PoolVx == 0 && w.PoolVy == 0) (w.PoolX, w.PoolY) = (r.X, r.Y);
                    w.PoolVx += pull.Ax * dt; w.PoolVy += pull.Ay * dt;
                    var damp = Math.Max(0, 1 - 2.4 * dt);
                    w.PoolVx *= damp; w.PoolVy *= damp;
                    if (Math.Abs(w.PoolVx) < 0.5 && Math.Abs(w.PoolVy) < 0.5 && !pull.Inside) (w.PoolVx, w.PoolVy) = (0, 0);
                    w.PoolX += w.PoolVx * dt; w.PoolY += w.PoolVy * dt;
                    w.PoolScale += ((pull.Inside ? pull.Scale : 1) - w.PoolScale) * (1 - Math.Exp(-8 * dt));
                    w.PoolEaten = pull.Eaten;
                    Active[i] = (w, new PixelRect((int)Math.Round(w.PoolX), (int)Math.Round(w.PoolY), r.Width, r.Height));
                    moved.Add(i);
                    displaced = true;
                }
            }
            var eaten = new List<(FlashOverlayWindow, PixelRect)>();
            foreach (var i in moved)
            {
                var (w, r) = Active[i];
                w.Position = new PixelPoint(r.X - w.GlowPadPx, r.Y - w.GlowPadPx);
                w.ApplyPoolScale();
                if (w.PoolEaten) eaten.Add((w, r));
            }
            foreach (var (w, r) in eaten) PoolAte(w, r);
            if (Active.Count == 0 || (!displaced && !PoolOn())) StopPoolTick();
        }

        /// <summary>WPF PoolAte (:2829): the eye took this flash. A card leaves the way a broken one does, and
        /// nothing is paid (owner 2026-11-15): no click event, no XP, no hydra, no haptics.</summary>
        private static void PoolAte(FlashOverlayWindow w, PixelRect r)
        {
            if (Active.RemoveAll(e => e.Window == w) == 0) return;
            if (w.Deck != null) w.DeckBreak = true;
            Leave(w, r, CoreSettings.Current);
        }
    }

    internal sealed partial class FlashOverlayWindow
    {
        /// <summary>The Whirlpool's hold on this flash (WPF FlashItem.PoolVx/PoolVy/PoolScale/PoolEaten): the pool
        /// moves the window itself, so clicks land where the picture is; the scale is draw-only.</summary>
        internal double PoolVx, PoolVy, PoolX, PoolY;
        internal double PoolScale = 1;
        internal bool PoolEaten;
        private ScaleTransform? _poolScale;

        internal void ApplyPoolScale()
        {
            if (_poolScale == null)
            {
                if (Math.Abs(PoolScale - 1) < 0.001 || _card.RenderTransform is not TransformGroup group) return;
                _poolScale = new ScaleTransform();
                group.Children.Add(_poolScale);
            }
            _poolScale.ScaleX = _poolScale.ScaleY = PoolScale;
        }
    }
}
