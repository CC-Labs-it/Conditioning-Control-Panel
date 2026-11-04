using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using ConditioningControlPanel.Avalonia.Helpers;
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
                double t = Math.Clamp(seconds / .26, 0, 1) - 1;
                double eased = 1 + 1.6 * t * t * t + .6 * t * t;
                scale.ScaleX = scale.ScaleY = .96 + .04 * eased;
            }) { OnParked = ParkBannerPop };
            _bannerPop.Start();
        }

        private BeatLoop? _bannerSparkles;
        private long _bannerSparkleRun;

        // WPF MainWindow.BannerFx.cs:346: one finite, staggered run on support arrival.
        private void RunBannerSparkles()
        {
            ClearBannerSparkles();
            var layer = this.FindControl<Canvas>("BannerSparkleLayer");
            var text = BannerPrimary;
            if (layer is null || text is null || !layer.IsEffectivelyVisible || layer.Bounds.Width <= 4) return;
            var origin = text.TranslatePoint(new Point(0, 0), layer);
            if (origin is null || text.Bounds.Width <= 4) return;
            var random = new Random();
            for (int i = 0; i < 7; i++)
            {
                double size = 11 + random.NextDouble() * 7;
                var sprite = new Path
                {
                    Width = size, Height = size, Fill = Brushes.White, Stretch = Stretch.Fill,
                    Data = Geometry.Parse("M .5,0 C .55,.4 .6,.45 1,.5 C .6,.55 .55,.6 .5,1 C .45,.6 .4,.55 0,.5 C .4,.45 .45,.4 .5,0 Z"),
                    Opacity = 0, IsHitTestVisible = false
                };
                Canvas.SetTop(sprite, layer.Bounds.Height / 2 - size / 2 + (random.NextDouble() * 2 - 1) * 7);
                layer.Children.Add(sprite);
            }
            var sprites = layer.Children.OfType<Path>().ToArray();
            long run = _bannerSparkleRun;
            double left = origin.Value.X, width = text.Bounds.Width;
            _bannerSparkles = new BeatLoop(layer, seconds =>
            {
                if (run != _bannerSparkleRun) return;
                if (!IsActive || WindowState == WindowState.Minimized || CoreSettings.Current.MotionLevel == MotionLevel.Off || seconds >= 1.79)
                {
                    ClearBannerSparkles();
                    return;
                }
                for (int i = 0; i < sprites.Length; i++)
                {
                    double t = Math.Clamp((seconds - i * .09) / 1.25, 0, 1);
                    double eased = (1 - Math.Cos(Math.PI * t)) / 2;
                    Canvas.SetLeft(sprites[i], left - 6 + width * eased - sprites[i].Width / 2);
                    sprites[i].Opacity = Math.Min(1, Math.Min(t / .12, (1 - t) / .2));
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
