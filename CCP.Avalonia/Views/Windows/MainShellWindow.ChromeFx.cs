// PORTED-IN-PART: WPF MainWindow.ChromeFx.cs. START clipping and banner message sheens are live.
// ponytail: the rest is wire-shell-chrome-fx-b. Restore in this order, on BeatLoop (no infinite
// animations), gated by ChromeBannerSheen/Pr4aAmbientAllowed:
//   1. START sheen and XP gloss - SweepStartSheen, _startSheenTimer, ApplyXpSheen, ParkSheen.
//   2. breathing glows - ApplyNavGlowBreath, ApplyStartButtonGlow and their Min/Max/Breath constants.
//   3. tab transition + card stagger - PORTED in MainShellWindow.TabTransitions.cs (except the
//      100 ms FadeOutgoingTab: the leaving tab is cut, see there); caller is ShowTab.
//   4. nav rail hover/active - NavButton_MouseEnter/Leave, NudgeNavIcon, ApplyNavActiveGlow,
//      SetNavIndicator, NavIconHoverScale/NavIconHoverMs.
//   5. XP meter - AnimateXpDisplay, FillXpBarTo, _lastXpShown; caller is the Progression XP refresh.
// Gone for good: _chromeFxWindowActive / OnChromeFxWindowStateish (re-expressed by Pr4aAmbientAllowed).

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System.Linq;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.UI;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>WPF BannerSheenSeconds (:47) and SweepBannerSheen's peak opacity.</summary>
        internal const double BannerSheenMs = 750, BannerSheenPeak = 0.22;

        private BeatLoop? _bannerSheenBeat;
        internal bool BannerSheenSweeping => _bannerSheenBeat?.IsRunning == true;
        private DateTime _lastBannerSheenUtc = DateTime.MinValue;

        private BeatLoop? _bannerAmbient;
        private long _bannerRepeatStart;
        private long _bannerAmbientRun;

        // WPF MainWindow.BannerFx.cs:166,205: breath and support repeat share the ambient gate.
        private void ApplyBannerAmbient()
        {
            var host = this.FindControl<Border>("HeaderBannerGlowHost");
            if (host is null) return;
            if (!Pr4aAmbientAllowed || !IsVisible || !host.IsEffectivelyVisible)
            {
                ++_bannerAmbientRun;
                _bannerAmbient?.Stop();
                _bannerAmbient = null;
                host.Opacity = .35;
                ClearBannerSparkles();
                return;
            }
            if (_bannerAmbient != null) return;
            _bannerRepeatStart = BeatLoop.Time.GetTimestamp();
            long run = ++_bannerAmbientRun;
            _bannerAmbient = new BeatLoop(host, seconds =>
            {
                if (run != _bannerAmbientRun) return;
                if (!Pr4aAmbientAllowed || !IsVisible) { ApplyBannerAmbient(); return; }
                host.Opacity = .25 + .35 * BeatLoop.Breath(seconds, 3.2);
                if (BeatLoop.Time.GetElapsedTime(_bannerRepeatStart).TotalSeconds < 12) return;
                _bannerRepeatStart = BeatLoop.Time.GetTimestamp();
                if (_bannerCurrentIndex >= 0 && _bannerCurrentIndex < _bannerBeats.Length
                    && ReferenceEquals(_bannerBeats[_bannerCurrentIndex], BannerPrimary)) RunBannerSparkles();
            }) { OnParked = () => { if (run == _bannerAmbientRun) ApplyBannerAmbient(); } };
            _bannerAmbient.Start();
        }

        private void InitializeChromeFx()
        {
            // Message sheen is finite. The sibling glow and repeat use the ambient gate.
            // Paint the sibling shadow once per theme change; only its opacity breathes.
            if (this.FindControl<Border>("HeaderBannerGlowHost") is { } glowHost)
            {
                void PaintGlow()
                {
                    var color = glowHost.Tag is Color c ? c : Color.FromRgb(255, 105, 180);
                    glowHost.BoxShadow = new BoxShadows(new BoxShadow { Blur = 12, Color = color });
                }
                glowHost.PropertyChanged += (_, e) => { if (e.Property == Control.TagProperty) PaintGlow(); };
                PaintGlow();
            }
            Activated += (_, _) => ApplyBannerAmbient();
            Deactivated += (_, _) => { ParkBannerSheen(); ApplyBannerAmbient(); };
            // Opened fires again after every tray Hide/Show; never stack the static subscription.
            Opened += (_, _) =>
            {
                AmbientFxCanvas.Env.MotionGateChanged -= ApplyBannerAmbient;
                AmbientFxCanvas.Env.MotionGateChanged += ApplyBannerAmbient;
                ApplyBannerAmbient();
            };
            Closed += (_, _) => { AmbientFxCanvas.Env.MotionGateChanged -= ApplyBannerAmbient; ++_bannerAmbientRun; _bannerAmbient?.Stop(); _bannerAmbient = null; ClearBannerSparkles(); };
            Closed += (_, _) => ParkBannerSheen();
            PropertyChanged += (_, e) =>
            {
                if (e.Property == WindowStateProperty || e.Property == IsVisibleProperty) ApplyBannerAmbient();
                if ((e.Property == WindowStateProperty && WindowState == WindowState.Minimized)
                    || (e.Property == IsVisibleProperty && !IsVisible)) ParkBannerSheen();
            };
        }

        /// <summary>WPF SweepBannerSheen: message rotations, announcements and the Home marquee's
        /// return share one finite sweep and the Core twenty-second throttle (force bypasses it).</summary>
        internal void SweepBannerSheen(bool force = false)
        {
            try
            {
                var now = BeatLoop.Time.GetUtcNow().UtcDateTime;
                if (!ChromeBannerSheen.CanSweep(IsActive && WindowState != WindowState.Minimized,
                    CoreSettings.Current.MotionLevel, force, now, _lastBannerSheenUtc)) return;
                var host = this.FindControl<Grid>("BannerSheenHost");
                var band = this.FindControl<Border>("BannerSheen");
                var slide = (band?.RenderTransform as TransformGroup)?.Children.OfType<TranslateTransform>().FirstOrDefault();
                if (host is null || band is null || slide is null || host.Bounds.Width <= 0 || !band.IsEffectivelyVisible) return;
                _lastBannerSheenUtc = now;
                ParkBannerSheen();
                double bandWidth = band.Width;
                _bannerSheenBeat = new BeatLoop(band, seconds =>
                {
                    if (!IsActive || WindowState == WindowState.Minimized || CoreSettings.Current.MotionLevel == Models.MotionLevel.Off)
                    {
                        ParkBannerSheen();
                        return;
                    }
                    double progress = Math.Clamp(seconds * 1000 / BannerSheenMs, 0, 1);
                    double eased = (1 - Math.Cos(Math.PI * progress)) / 2;
                    slide.X = -bandWidth + (host.Bounds.Width + bandWidth * 1.25) * eased;
                    band.Opacity = BeatLoop.Keys(progress, new[] { (0.0, 0.0), (0.20, BannerSheenPeak), (0.75, BannerSheenPeak), (1.0, 0.0) });
                    if (progress >= 1) ParkBannerSheen();
                }) { OnParked = ParkBannerSheen };
                _bannerSheenBeat.Start();
            }
            catch (Exception ex) { ParkBannerSheen(); Log.Debug("SweepBannerSheen: {E}", ex.Message); }
        }

        private void ParkBannerSheen()
        {
            _bannerSheenBeat?.Stop();
            _bannerSheenBeat = null;
            if (this.FindControl<Border>("BannerSheen") is not { } band) return;
            band.Opacity = 0;
            if ((band.RenderTransform as TransformGroup)?.Children.OfType<TranslateTransform>().FirstOrDefault() is { } slide)
                slide.X = 0;
        }

        /// <summary>Corner radius of the START button, and therefore of the sheen host's clip.
        /// Must MATCH the button's own rounding or the band's corners show through.</summary>
        private const double StartSheenCornerRadius = 8;

        /// <summary>
        /// Clips the START sheen host to the button's rounded rect, so the band cannot poke out of
        /// the corners as it sweeps. Named by MainShellWindow.axaml:2682 and driven by layout, so
        /// this is live whether or not the sweep itself is ever restored - and it has to be: the
        /// host is a sibling overlay sharing the button's cell, not button content, so nothing
        /// else clips it.
        /// </summary>
        private void StartSheenHost_SizeChanged(object? sender, SizeChangedEventArgs e)
        {
            try
            {
                if (sender is not Control host) return;
                var size = e.NewSize;
                if (size.Width <= 0 || size.Height <= 0) { host.Clip = null; return; }
                host.Clip = new RectangleGeometry(
                    new Rect(0, 0, size.Width, size.Height),
                    StartSheenCornerRadius, StartSheenCornerRadius);
            }
            catch (Exception ex) { Log.Debug("StartSheenHost_SizeChanged: {E}", ex.Message); }
        }
    }
}
