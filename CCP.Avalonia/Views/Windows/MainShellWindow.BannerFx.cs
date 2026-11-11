using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private BeatLoop? _bannerPop;
        private TextBlock? _poppingBanner;

        // WPF MainWindow.BannerFx.cs:236,277: finite pop on each active beat change.
        private void PopBannerBeat(TextBlock incoming)
        {
            ParkBannerPop();
            if (!IsActive || WindowState == WindowState.Minimized || CoreSettings.Current.MotionLevel == MotionLevel.Off) return;
            if (ReferenceEquals(incoming, BannerPrimary))
            {
                SweepBannerSheen(force: true);
                RunBannerSparkles();
                _bannerRepeatStart = BeatLoop.Time.GetTimestamp();
            }
            var scale = new ScaleTransform();
            incoming.RenderTransformOrigin = RelativePoint.Center;
            incoming.RenderTransform = scale;
            _poppingBanner = incoming;
            _bannerPop = new BeatLoop(incoming, seconds =>
            {
                if (!IsActive || WindowState == WindowState.Minimized || CoreSettings.Current.MotionLevel == MotionLevel.Off || seconds >= .26)
                {
                    ParkBannerPop();
                    return;
                }
                // BackEase Out, amplitude 0.6, from 0.96 to 1 over 260 ms.
                double remaining = 1 - Math.Clamp(seconds / .26, 0, 1);
                double eased = 1 - (remaining * remaining * remaining - .6 * remaining * Math.Sin(Math.PI * remaining));
                scale.ScaleX = scale.ScaleY = .96 + .04 * eased;
            }) { OnParked = ParkBannerPop };
            _bannerPop.Start();
        }

        private static readonly (double At, double V)[] SparkleTwinkle =
            { (0, 0), (.15, 1), (.4, .5), (.65, 1.1), (1, 0) };
        private BeatLoop? _bannerSparkles;
        private long _bannerSparkleRun;

        // WPF MainWindow.BannerFx.cs:346: one finite, staggered run on support arrival.
        private void RunBannerSparkles()
        {
            ClearBannerSparkles();
            if (!AmbientFxCanvas.Env.AllowTransitions) return;
            var layer = this.FindControl<Canvas>("BannerSparkleLayer");
            var text = BannerPrimary;
            if (layer is null || text is null || !layer.IsEffectivelyVisible || layer.Bounds.Width <= 4) return;
            var origin = text.TranslatePoint(new Point(0, 0), layer);
            // WPF centres TxtBannerPrimary, so ActualWidth is the support run only. Here it
            // stretches to the widest beat, so fly over the centred desired width instead.
            double width = Math.Min(text.DesiredSize.Width, text.Bounds.Width);
            if (origin is null || width <= 4) return;
            double left = origin.Value.X + (text.Bounds.Width - width) / 2;
            var random = new Random();
            var sprites = new (Grid Root, ScaleTransform Scale, RotateTransform Rotate, double From, double To, double Spin)[7];
            var glow = this.TryFindResource("PinkColor", out var resource) && resource is Color color
                ? color : Color.FromRgb(255, 105, 180);
            for (int i = 0; i < sprites.Length; i++)
            {
                double size = 11 + random.NextDouble() * 7;
                double lane = (random.NextDouble() * 2 - 1) * Math.Min(7, layer.Bounds.Height * .3);
                double from = left - 6 + random.NextDouble() * width * .12;
                double to = left + width * (.82 + random.NextDouble() * .2);
                double spin = (random.NextDouble() < .5 ? -1 : 1) * (90 + random.NextDouble() * 120);
                var halo = new RadialGradientBrush();
                halo.GradientStops.Add(new GradientStop(Color.FromArgb(200, glow.R, glow.G, glow.B), 0));
                halo.GradientStops.Add(new GradientStop(Color.FromArgb(90, glow.R, glow.G, glow.B), .35));
                halo.GradientStops.Add(new GradientStop(Color.FromArgb(0, glow.R, glow.G, glow.B), 1));
                var scale = new ScaleTransform();
                var rotate = new RotateTransform();
                var transform = new TransformGroup();
                transform.Children.Add(scale); transform.Children.Add(rotate);
                var sprite = new Grid { Width = size, Height = size, Opacity = 0, IsHitTestVisible = false,
                    RenderTransformOrigin = RelativePoint.Center, RenderTransform = transform };
                sprite.Children.Add(new Ellipse { Fill = halo });
                sprite.Children.Add(new Path { Width = size * .72, Height = size * .72, Fill = Brushes.White,
                    Stretch = Stretch.Fill, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                    Data = Geometry.Parse("M .5,0 C .55,.4 .6,.45 1,.5 C .6,.55 .55,.6 .5,1 C .45,.6 .4,.55 0,.5 C .4,.45 .45,.4 .5,0 Z") });
                Canvas.SetTop(sprite, layer.Bounds.Height / 2 + lane - size / 2);
                layer.Children.Add(sprite);
                sprites[i] = (sprite, scale, rotate, from, to, spin);
            }
            long run = _bannerSparkleRun;
            _bannerSparkles = new BeatLoop(layer, seconds =>
            {
                if (run != _bannerSparkleRun) return;
                if (!IsActive || WindowState == WindowState.Minimized || !AmbientFxCanvas.Env.AllowTransitions || seconds >= 1.79)
                {
                    ClearBannerSparkles();
                    return;
                }
                for (int i = 0; i < sprites.Length; i++)
                {
                    double t = Math.Clamp((seconds - i * .09) / 1.25, 0, 1);
                    double eased = (1 - Math.Cos(Math.PI * t)) / 2;
                    var sprite = sprites[i];
                    Canvas.SetLeft(sprite.Root, sprite.From + (sprite.To - sprite.From) * eased - sprite.Root.Width / 2);
                    sprite.Root.Opacity = Math.Min(1, Math.Min(t / .12, (1 - t) / .2));
                    double twinkle = BeatLoop.Keys(t, SparkleTwinkle);
                    sprite.Scale.ScaleX = sprite.Scale.ScaleY = twinkle;
                    sprite.Rotate.Angle = sprite.Spin * t;
                }
            }) { OnParked = () => { if (run == _bannerSparkleRun) ClearBannerSparkles(); } };
            _bannerSparkles.Start();
        }

        private void ClearBannerSparkles()
        {
            ++_bannerSparkleRun;
            _bannerSparkles?.Stop();
            _bannerSparkles = null;
            this.FindControl<Canvas>("BannerSparkleLayer")?.Children.Clear();
        }

        private void ParkBannerPop()
        {
            _bannerPop?.Stop();
            _bannerPop = null;
            if (_poppingBanner is { } beat)
            {
                beat.ClearValue(Visual.RenderTransformProperty);
                beat.ClearValue(Visual.RenderTransformOriginProperty);
            }
            _poppingBanner = null;
        }
    }
}
