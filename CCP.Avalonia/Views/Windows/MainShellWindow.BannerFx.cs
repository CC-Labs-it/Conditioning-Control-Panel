using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
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
            if (ReferenceEquals(incoming, BannerPrimary)) SweepBannerSheen(force: true);
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
