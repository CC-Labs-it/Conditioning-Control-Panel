using System;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private BeatLoop? _bannerFlash;
        private void FlashBannerRing()
        {
            ParkBannerFlash();
            var ring = this.FindControl<Border>("BannerFlashRing");
            var glow = this.FindControl<Border>("HeaderBannerGlowHost");
            if (ring is null || glow is null) return;
            _bannerFlash = new BeatLoop(ring, t =>
            {
                if (!IsActive || !IsVisible || WindowState == WindowState.Minimized || CoreSettings.Current.MotionLevel == MotionLevel.Off || t >= .4)
                { ParkBannerFlash(); return; }
                ring.Opacity = t <= .07 ? .95 * t / .07 : .95 * Math.Pow((.4 - t) / .33, 2);
                double blur = t <= .09 ? 12 + 10 * t / .09 : 12 + 10 * Math.Pow((.4 - t) / .31, 2);
                SetBannerGlowBlur(glow, blur);
            }) { OnParked = ParkBannerFlash };
            _bannerFlash.Start();
        }

        private static void SetBannerGlowBlur(Border glow, double blur)
        {
            var color = glow.Tag is Color c ? c : Color.FromRgb(255, 105, 180);
            glow.BoxShadow = new BoxShadows(new BoxShadow { Blur = blur, Color = color });
        }

        private void ParkBannerFlash()
        {
            var clock = _bannerFlash;
            _bannerFlash = null;
            clock?.Stop();
            if (this.FindControl<Border>("BannerFlashRing") is { } ring) ring.Opacity = 0;
            if (this.FindControl<Border>("HeaderBannerGlowHost") is { } glow) SetBannerGlowBlur(glow, 12);
        }
    }
}
