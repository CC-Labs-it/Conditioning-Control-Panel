// Header quest stamps: the finite motion of WPF MainWindow.QuestStamps.cs (PlayStampStampedPop :540,
// ZoomStamp :576, ShowStampPopup :889) and MotionFx.HoverLift (:74), on one stepped tween clock.
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        internal static TimeProvider QuestStampFxTime = TimeProvider.System;

        private sealed class StampTween
        {
            public object Target = null!;
            public int Prop;
            public long Start;
            public double Ms, From, To;
            public Func<double, double> Ease = t => t;
            public Action<double> Set = null!;
            public Action? Done;
        }

        private readonly List<StampTween> _stampTweens = new();
        private FrameClock? _stampFxClock;
        private TranslateTransform? _stampPopSlide;
        private readonly Dictionary<string, bool> _stampCompletedSeen = new(StringComparer.Ordinal);

        private const int PropScaleX = 0, PropScaleY = 1, PropAngle = 2, PropOpacity = 3, PropSlide = 4;

        private static bool StampMotion => AmbientFxCanvas.Env.AllowTransitions;
        private static double StampCubicOut(double t) { double x = 1 - t; return 1 - x * x * x; }
        private static double StampQuadOut(double t) => 1 - (1 - t) * (1 - t);
        // WPF BackEase EaseOut, Amplitude 0.6: 1 - f(1 - t), f(x) = x^3 - x * A * sin(pi x).
        private static double StampBackOut(double t) { double x = 1 - t; return 1 - (x * x * x - x * 0.6 * Math.Sin(Math.PI * x)); }

        /// <summary>Test hook: tweens still running.</summary>
        internal int QuestStampTweens => _stampTweens.Count;

        private void StampAnimate(object target, int prop, double from, double to, int ms, Action<double> set,
                                  Func<double, double>? ease = null, Action? done = null)
        {
            _stampTweens.RemoveAll(t => ReferenceEquals(t.Target, target) && t.Prop == prop);
            if (!StampMotion || _questStampsClosed) { set(to); done?.Invoke(); return; }
            _stampTweens.Add(new StampTween
            {
                Target = target, Prop = prop, Start = QuestStampFxTime.GetTimestamp(), Ms = ms,
                From = from, To = to, Ease = ease ?? (t => t), Set = set, Done = done,
            });
            set(from);
            if (_stampFxClock == null)
            {
                // Finite interaction motion at render rate, frame-locked like WPF's animation clock.
                _stampFxClock = new FrameClock(this) { Interval = TimeSpan.FromSeconds(1.0 / 60) };
                _stampFxClock.Tick += (_, _) => StepQuestStampFx();
            }
            _stampFxClock.Start();
        }

        private void StampStop(object target, params int[] props) =>
            _stampTweens.RemoveAll(t => ReferenceEquals(t.Target, target) && Array.IndexOf(props, t.Prop) >= 0);

        /// <summary>One clock tick at <see cref="QuestStampFxTime"/>'s now.</summary>
        internal void StepQuestStampFx()
        {
            for (int i = _stampTweens.Count - 1; i >= 0; i--)
            {
                if (i >= _stampTweens.Count) continue;
                var tw = _stampTweens[i];
                double t = Math.Clamp(QuestStampFxTime.GetElapsedTime(tw.Start).TotalMilliseconds / tw.Ms, 0, 1);
                tw.Set(tw.From + (tw.To - tw.From) * tw.Ease(t));
                if (t < 1) continue;
                _stampTweens.Remove(tw);
                try { tw.Done?.Invoke(); } catch { /* teardown */ }
            }
            if (_stampTweens.Count == 0 || _questStampsClosed) { _stampTweens.Clear(); _stampFxClock?.Stop(); }
        }

        /// <summary>WPF ZoomStamp: swell to 1.5 and relax the tilt to a quarter, 170 ms in / 200 ms out, cubic out.</summary>
        private void ZoomStamp(QuestStampInfo? info, bool on)
        {
            if (info?.Cell == null || info.Scale == null || info.Rotate == null) return;
            info.Cell.ZIndex = on ? 20 : 0;
            var scale = info.Scale;
            var rotate = info.Rotate;
            double toScale = on ? QuestStampHoverScale : 1;
            double toAngle = QuestStampRestAngle(info.Key) * (on ? 0.25 : 1);
            int ms = on ? 170 : 200;
            StampAnimate(scale, PropScaleX, scale.ScaleX, toScale, ms, v => scale.ScaleX = v, StampCubicOut);
            StampAnimate(scale, PropScaleY, scale.ScaleY, toScale, ms, v => scale.ScaleY = v, StampCubicOut);
            StampAnimate(rotate, PropAngle, rotate.Angle, toAngle, ms, v => rotate.Angle = v, StampCubicOut);
        }

        /// <summary>WPF MotionFx.HoverLift: 1.02, 150 ms quad out.</summary>
        private void LiftStampHost(bool on)
        {
            var s = _stampHostScale;
            double to = on ? 1.02 : 1;
            StampAnimate(s, PropScaleX, s.ScaleX, to, 150, v => s.ScaleX = v, StampQuadOut);
            StampAnimate(s, PropScaleY, s.ScaleY, to, 150, v => s.ScaleY = v, StampQuadOut);
        }

        /// <summary>WPF ShowStampPopup's entrance: fade 0 -> 1 in 150 ms, slide -7 -> 0 in 180 ms cubic out.</summary>
        private void AnimateStampPopupIn(bool animate)
        {
            if (_stampPopCard is not { } card || _stampPopSlide is not { } slide) return;
            if (!animate || !StampMotion)
            {
                StampStop(card, PropOpacity);
                StampStop(slide, PropSlide);
                card.Opacity = 1;
                slide.Y = 0;
                return;
            }
            StampAnimate(card, PropOpacity, 0, 1, 150, v => card.Opacity = v);
            StampAnimate(slide, PropSlide, -7, 0, 180, v => slide.Y = v, StampCubicOut);
        }

        /// <summary>
        /// WPF AddStamp: only a not-done -> done TRANSITION pops; the first paint of a session
        /// records state silently.
        /// </summary>
        private void TrackStampCompletion(QuestStampInfo info)
        {
            bool known = _stampCompletedSeen.TryGetValue(info.Key, out var wasDone);
            _stampCompletedSeen[info.Key] = info.Completed;
            if (info.Completed && known && !wasDone) PlayStampStampedPop(info);
        }

        /// <summary>
        /// WPF PlayStampStampedPop: scale 0.7 -> 1.35 (back out, 45 %) -> 1.0 (cubic out) over 420 ms,
        /// plus a gold glow (FFD700, blur 16) fading 0.9 -> 0 over 650 ms and then removed. The glow is a
        /// sibling Border's BoxShadow under the plate, not an Effect on the tile (P82).
        /// </summary>
        private void PlayStampStampedPop(QuestStampInfo info)
        {
            try
            {
                if (!StampMotion || info.Cell is not { } cell || info.Scale is not { } scale) return;
                static double Pop(double t) => t < 0.45
                    ? 0.7 + (1.35 - 0.7) * StampBackOut(t / 0.45)
                    : 1.35 + (1.0 - 1.35) * StampCubicOut((t - 0.45) / 0.55);
                StampAnimate(scale, PropScaleX, 0, 1, 420, v => scale.ScaleX = Pop(v));
                StampAnimate(scale, PropScaleY, 0, 1, 420, v => scale.ScaleY = Pop(v));

                var flash = new Border
                {
                    CornerRadius = new CornerRadius(3),
                    BoxShadow = new BoxShadows(new BoxShadow { Color = Color.FromRgb(0xFF, 0xD7, 0x00), Blur = 16 }),
                    Opacity = 0.9,
                    IsHitTestVisible = false,
                    Tag = "stamp-flash",
                };
                cell.Children.Insert(0, flash);
                StampAnimate(flash, PropOpacity, 0.9, 0, 650, v => flash.Opacity = v,
                    done: () => cell.Children.Remove(flash));
            }
            catch (Exception ex) { Log.Debug("[QuestStamps] pop failed: {E}", ex.Message); }
        }
    }
}
