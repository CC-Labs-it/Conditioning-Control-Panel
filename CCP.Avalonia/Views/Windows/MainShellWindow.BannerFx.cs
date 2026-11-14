// WPF MainWindow.BannerFx.cs:166-236, 277-433 (Nav polish waves 6 and 13): the banner's beat pop, ring
// flash, support-beat sparkle run and the glow breath with its 12 s sparkle repeat.
// Restack b222 hand-port of Mich's #4283 #4308 #4631 onto the trunk's BannerDrum: the pop shares the
// drum's ScaleTransform (WPF BeatTransforms), so a rolled beat only widens, as in WPF.
// Gates: finite beat FX run while the window is active and motion is not Off (WPF OnBeatChange); the
// breath and the repeat ride ChromeAmbientAllowed (WPF ApplyBannerFxLoops). House Gentle rule: the ring
// and the sparkles rise faster than GentleVisuals.RiseFloorMs, so neither runs while Gentle visuals are on.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Safety;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // WPF BannerFxRules, verbatim.
        internal const double BannerPopFrom = 0.96, BannerPopSeconds = 0.26, BannerFlashSeconds = 0.4;
        internal const double BannerBreathMin = 0.25, BannerBreathMax = 0.6, BannerBreathSeconds = 3.2, BannerGlowRest = 0.35;
        internal const double BannerGlowBlur = 12, BannerGlowFlareBlur = 22;
        internal const int BannerSparkleCount = 7;
        internal const double BannerSparkleRunSeconds = 1.25, BannerSparkleStaggerSeconds = 0.09, BannerSparkleRepeatSeconds = 12;
        private static readonly (double At, double V)[] SparkleTwinkle = { (0, 0), (.15, 1), (.4, .5), (.65, 1.1), (1, 0) };
        private static readonly (double At, double V)[] SparkleAlpha = { (0, 0), (.12, 1), (.8, 1), (1, 0) };

        private BeatLoop? _bannerPop, _bannerFlash, _bannerSparkles, _bannerAmbient;
        private TextBlock? _poppingBeat;
        private ScaleTransform? _popScale;
        private bool _popY, _popOwnsTransform;
        private long _bannerSparkleRun, _bannerRepeatFrom;
        private int _bannerSparkleSeed = Environment.TickCount;

        internal bool BannerPopping => _bannerPop?.IsRunning == true;
        internal bool BannerFlashing => _bannerFlash?.IsRunning == true;
        internal bool BannerBreathing => _bannerAmbient?.IsRunning == true;
        internal int BannerSparkleSprites => Named<Canvas>("BannerSparkleLayer")?.Children.Count ?? 0;

        /// <summary>WPF BannerFxRules.OnBeatChange: no beat FX on an inactive or minimised window or at motion Off.</summary>
        private bool BannerBeatFxAllowed => !_chromeClosed && IsActive && WindowState != WindowState.Minimized
            && Env.Level != MotionLevel.Off;

        private bool SupportBeatOnScreen => _bannerCurrentIndex >= 0 && _bannerCurrentIndex < _bannerBeats.Length
            && ReferenceEquals(_bannerBeats[_bannerCurrentIndex], BannerPrimary);

        /// <summary>WPF OnBannerBeatChanged (:236): the incoming beat pops, the pill flashes, and the support
        /// beat gets a sparkle run that restarts the repeat clock. The rotation tick owns the sheen.</summary>
        internal void OnBannerBeatChanged(TextBlock incoming, bool rolled)
        {
            if (!BannerBeatFxAllowed) return;
            PopBannerBeat(incoming, popY: !rolled);
            if (GentleVisuals.On) return;
            FlashBannerRing();
            if (!ReferenceEquals(incoming, BannerPrimary)) return;
            RunBannerSparkles();
            _bannerRepeatFrom = BeatLoop.Time.GetTimestamp();
        }

        /// <summary>WPF PopBannerBeat (:277): 0.96 to 1 on BackEase Out (amplitude 0.6) over 260 ms. A rolled
        /// beat keeps the drum's height and only widens.</summary>
        private void PopBannerBeat(TextBlock beat, bool popY)
        {
            ParkBannerPop();
            if (beat.RenderTransform is TransformGroup { Children: [ScaleTransform shared, ..] })
            {
                _popScale = shared;
                _popOwnsTransform = false;
            }
            else
            {
                _popScale = new ScaleTransform();
                beat.RenderTransformOrigin = RelativePoint.Center;
                beat.RenderTransform = _popScale;
                _popOwnsTransform = true;
            }
            _poppingBeat = beat;
            _popY = popY;
            var scale = _popScale;
            void Step(double t)
            {
                if (t >= BannerPopSeconds || !BannerBeatFxAllowed) { ParkBannerPop(); return; }
                double r = 1 - t / BannerPopSeconds;
                double eased = 1 - (r * r * r - .6 * r * Math.Sin(Math.PI * r));
                double v = BannerPopFrom + (1 - BannerPopFrom) * eased;
                scale.ScaleX = v;
                if (popY) scale.ScaleY = v;
            }
            _bannerPop = new BeatLoop(beat, Step) { OnParked = ParkBannerPop };
            _bannerPop.Start();
            Step(0);
        }

        private void ParkBannerPop()
        {
            var loop = _bannerPop;
            _bannerPop = null;
            loop?.Stop();
            if (_popScale is { } scale)
            {
                scale.ScaleX = 1;
                if (_popY) scale.ScaleY = 1;
            }
            if (_popOwnsTransform && _poppingBeat is { } beat && ReferenceEquals(beat.RenderTransform, _popScale))
            {
                beat.ClearValue(Visual.RenderTransformProperty);
                beat.ClearValue(Visual.RenderTransformOriginProperty);
            }
            _poppingBeat = null;
            _popScale = null;
            _popOwnsTransform = false;
        }

        /// <summary>WPF FlashBannerRing (:322): the ring to 0.95 in 70 ms, out by 400 ms (QuadraticEase Out); the
        /// glow's blur, not its opacity, flares 12 to 22 at 90 ms and back, so the breath keeps its clock.</summary>
        private void FlashBannerRing()
        {
            ParkBannerFlash();
            if (Named<Border>("BannerFlashRing") is not { } ring) return;
            var glow = Named<Border>("HeaderBannerGlowHost");
            _bannerFlash = new BeatLoop(ring, t =>
            {
                if (t >= BannerFlashSeconds || !BannerBeatFxAllowed || GentleVisuals.On) { ParkBannerFlash(); return; }
                ring.Opacity = t <= .07 ? .95 * t / .07 : .95 * Math.Pow((BannerFlashSeconds - t) / .33, 2);
                if (glow is not null)
                    PaintBannerGlow(glow, t <= .09
                        ? BannerGlowBlur + (BannerGlowFlareBlur - BannerGlowBlur) * t / .09
                        : BannerGlowBlur + (BannerGlowFlareBlur - BannerGlowBlur) * Math.Pow((BannerFlashSeconds - t) / .31, 2));
            }) { OnParked = ParkBannerFlash };
            _bannerFlash.Start();
        }

        private void ParkBannerFlash()
        {
            var loop = _bannerFlash;
            _bannerFlash = null;
            loop?.Stop();
            if (Named<Border>("BannerFlashRing") is { } ring) ring.Opacity = 0;
            if (Named<Border>("HeaderBannerGlowHost") is { } glow) PaintBannerGlow(glow, BannerGlowBlur);
        }

        private void PaintBannerGlow(Border glow, double blur)
        {
            var color = this.TryFindResource("PinkColor", out var found) && found is Color c ? c : Color.FromRgb(0xFF, 0x69, 0xB4);
            glow.BoxShadow = new BoxShadows(new BoxShadow { Blur = blur, Color = color });
        }

        /// <summary>WPF ApplyBannerFxLoops (:166), called from ApplyChromeFxLoops: the glow breathes 0.25-0.6
        /// over 3.2 s (Alternate, SineEaseInOut) and the support beat repeats its sparkle run every 12 s, both
        /// only while the chrome's ambient gate allows; otherwise the glow rests at 0.35 and sparkles clear.</summary>
        private void ApplyBannerFxLoops()
        {
            if (Named<Border>("HeaderBannerGlowHost") is not { } glow) return;
            if (!BannerFlashing) PaintBannerGlow(glow, BannerGlowBlur);
            if (!ChromeAmbientAllowed)
            {
                StopBannerLoops();
                glow.Opacity = BannerGlowRest;
                return;
            }
            if (BannerBreathing) return;
            _bannerRepeatFrom = BeatLoop.Time.GetTimestamp();
            _bannerAmbient = new BeatLoop(glow, t =>
            {
                glow.Opacity = BannerBreathMin + (BannerBreathMax - BannerBreathMin) * BeatLoop.Breath(t, BannerBreathSeconds);
                if (BeatLoop.Time.GetElapsedTime(_bannerRepeatFrom).TotalSeconds < BannerSparkleRepeatSeconds) return;
                _bannerRepeatFrom = BeatLoop.Time.GetTimestamp();
                if (SupportBeatOnScreen && !GentleVisuals.On) RunBannerSparkles();
            });
            _bannerAmbient.Start();
        }

        private void StopBannerLoops()
        {
            _bannerAmbient?.Stop();
            _bannerAmbient = null;
            ClearBannerSparkles();
        }

        /// <summary>WPF RunBannerSparkles (:346) + BannerFxRules.PlanRun: seven stars fly left to right over the
        /// support run, staggered 90 ms, each 1.25 s (SineEaseInOut travel, twinkle, spin), then the run removes
        /// itself. The support TextBlock stretches here (WPF centres it), so the run flies over its centred
        /// desired width.</summary>
        private void RunBannerSparkles()
        {
            if (!Env.AllowTransitions || GentleVisuals.On) return;
            var layer = Named<Canvas>("BannerSparkleLayer");
            var text = BannerPrimary;
            if (layer is null || text is null || !layer.IsEffectivelyVisible || layer.Bounds.Height <= 0) return;
            double width = Math.Min(text.DesiredSize.Width, text.Bounds.Width);
            if (width <= 4 || text.TranslatePoint(default, layer) is not { } origin) return;
            ClearBannerSparkles();
            double left = origin.X + (text.Bounds.Width - width) / 2, mid = layer.Bounds.Height / 2;
            var glow = this.TryFindResource("PinkColor", out var found) && found is Color c ? c : Color.FromRgb(0xFF, 0x69, 0xB4);
            var rng = new Random(_bannerSparkleSeed++);
            var sprites = new (Grid Root, ScaleTransform Scale, RotateTransform Rotate, double From, double To, double Spin)[BannerSparkleCount];
            for (int i = 0; i < sprites.Length; i++)
            {
                double size = 11 + rng.NextDouble() * 7;
                double lane = (rng.NextDouble() * 2 - 1) * Math.Min(7, layer.Bounds.Height * .3);
                double from = left - 6 + rng.NextDouble() * width * .12;
                double to = left + width * (.82 + rng.NextDouble() * .2);
                double spin = (rng.NextDouble() < .5 ? -1 : 1) * (90 + rng.NextDouble() * 120);
                var sprite = SparkleSprite(size, glow, out var scale, out var rotate);
                Canvas.SetTop(sprite, mid + lane - size / 2);
                Canvas.SetLeft(sprite, from - size / 2);
                layer.Children.Add(sprite);
                sprites[i] = (sprite, scale, rotate, from, to, spin);
            }
            long run = _bannerSparkleRun;
            _bannerSparkles = new BeatLoop(layer, seconds =>
            {
                if (run != _bannerSparkleRun) return;
                if (seconds >= BannerSparkleRunSeconds + (BannerSparkleCount - 1) * BannerSparkleStaggerSeconds
                    || !IsActive || WindowState == WindowState.Minimized || !Env.AllowTransitions || GentleVisuals.On)
                { ClearBannerSparkles(); return; }
                for (int i = 0; i < sprites.Length; i++)
                {
                    var s = sprites[i];
                    double t = Math.Clamp((seconds - i * BannerSparkleStaggerSeconds) / BannerSparkleRunSeconds, 0, 1);
                    double eased = (1 - Math.Cos(Math.PI * t)) / 2;
                    Canvas.SetLeft(s.Root, s.From + (s.To - s.From) * eased - s.Root.Width / 2);
                    s.Root.Opacity = BeatLoop.Keys(t, SparkleAlpha);
                    s.Scale.ScaleX = s.Scale.ScaleY = BeatLoop.Keys(t, SparkleTwinkle);
                    s.Rotate.Angle = s.Spin * t;
                }
            }) { OnParked = () => { if (run == _bannerSparkleRun) ClearBannerSparkles(); } };
            _bannerSparkles.Start();
        }

        /// <summary>WPF BannerFxRules.CreateSparkleSprite: a pink halo under a four-point white star.</summary>
        private static Grid SparkleSprite(double size, Color glow, out ScaleTransform scale, out RotateTransform rotate)
        {
            var halo = new RadialGradientBrush();
            halo.GradientStops.Add(new GradientStop(Color.FromArgb(200, glow.R, glow.G, glow.B), 0));
            halo.GradientStops.Add(new GradientStop(Color.FromArgb(90, glow.R, glow.G, glow.B), .35));
            halo.GradientStops.Add(new GradientStop(Color.FromArgb(0, glow.R, glow.G, glow.B), 1));
            scale = new ScaleTransform(0, 0);
            rotate = new RotateTransform();
            var root = new Grid
            {
                Width = size, Height = size, Opacity = 0, IsHitTestVisible = false,
                RenderTransformOrigin = RelativePoint.Center,
                RenderTransform = new TransformGroup { Children = { scale, rotate } },
            };
            root.Children.Add(new Ellipse { Fill = halo });
            root.Children.Add(new Path { Width = size * .72, Height = size * .72, Fill = Brushes.White, Stretch = Stretch.Fill,
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                Data = Geometry.Parse("M .5,0 C .55,.4 .6,.45 1,.5 C .6,.55 .55,.6 .5,1 C .45,.6 .4,.55 0,.5 C .4,.45 .45,.4 .5,0 Z") });
            return root;
        }

        private void ClearBannerSparkles()
        {
            ++_bannerSparkleRun;
            var loop = _bannerSparkles;
            _bannerSparkles = null;
            loop?.Stop();
            Named<Canvas>("BannerSparkleLayer")?.Children.Clear();
        }

        /// <summary>Close and park: every banner loop stops and the paint goes back to rest.</summary>
        private void StopBannerFx()
        {
            ParkBannerPop();
            ParkBannerFlash();
            StopBannerLoops();
        }
    }
}
