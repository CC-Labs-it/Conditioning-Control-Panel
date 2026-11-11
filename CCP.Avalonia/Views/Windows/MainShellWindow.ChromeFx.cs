// WPF MainWindow.ChromeFx.cs:42-48, 106-114, 158-184, 535-648, 725-755.
// START breath/sheen and XP gloss use the existing shared beat.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System.Linq;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Models;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;
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

        // WPF MainWindow.ChromeFx.cs:41-48, verbatim.
        private const double StartGlowMinOpacity = 0.32;
        private const double StartGlowMaxOpacity = 0.72;
        private const double StartGlowBreathSeconds = 4.6;
        private const double StartSheenSeconds = 0.85;
        private const int StartSheenIntervalSeconds = 8;
        private const double XpSheenSeconds = 6.0;
        private static readonly double[] XpSheenStarts = { -0.30, -0.15, 0.00 };

        private BeatLoop? _startChromeBeat;
        private BeatLoop? _xpGlossBeat;
        private long _startSheenCycle = -1;
        private bool _startSheenThisCycle;

        /// <summary>WPF ChromeAmbientAllowed: window focus + not minimised + motion and tier.</summary>
        internal bool ChromeAmbientAllowed => !_chromeClosed && IsVisible && IsActive && WindowState != WindowState.Minimized && Env.AllowAmbientLoops;

        internal bool StartChromeBreathing => _startChromeBeat?.IsRunning == true;
        internal bool XpGlossRunning => _xpGlossBeat?.IsRunning == true;

        private bool _chromeClosed;

        private void InitializeChromeFx()
        {
            // A message sheen is finite interaction motion, so Reduced and low performance
            // retain it as WPF does. Ambient loops use the stricter gate below.
            Activated += (_, _) => ApplyChromeFxLoops();
            Deactivated += (_, _) => { ParkBannerSheen(); ApplyChromeFxLoops(); };
            Opened += (_, _) =>
            {
                Env.MotionGateChanged -= ApplyChromeFxLoops;
                Env.MotionGateChanged += ApplyChromeFxLoops;
                CoreMods.ModChanged -= RefreshChromePalette;
                CoreMods.ModChanged += RefreshChromePalette;
                ApplyChromeFxLoops();
            };
            Closed += (_, _) =>
            {
                _chromeClosed = true;
                ParkBannerSheen();
                Env.MotionGateChanged -= ApplyChromeFxLoops;
                CoreMods.ModChanged -= RefreshChromePalette;
                StopChromeLoops();
            };
            PropertyChanged += (_, e) =>
            {
                if ((e.Property == WindowStateProperty && WindowState == WindowState.Minimized)
                    || (e.Property == IsVisibleProperty && !IsVisible)) ParkBannerSheen();
                if (e.Property == WindowStateProperty || e.Property == IsActiveProperty
                    || e.Property == IsVisibleProperty) ApplyChromeFxLoops();
            };
        }

        /// <summary>WPF ApplyChromeFxLoops (:161): the START glow + sheen and the XP gloss.</summary>
        private void ApplyChromeFxLoops()
        {
            try
            {
                ApplyStartButtonGlow();
                ApplyXpSheen();
            }
            catch (Exception ex) { Log.Debug("ApplyChromeFxLoops: {E}", ex.Message); }
        }

        private void RefreshChromePalette(object? sender, Models.ModPackage mod) =>
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (!_chromeClosed) ApplyChromeFxLoops(); });

        private void StopChromeLoops()
        {
            _startChromeBeat?.Stop();
            _startChromeBeat = null;
            _xpGlossBeat?.Stop();
            _xpGlossBeat = null;
        }

        /// <summary>
        /// WPF ApplyStartButtonGlow (:535) + the 8 s _startSheenTimer/SweepStartSheen (:590). The glow layer
        /// is hidden outright when the tier disallows glow or motion is Off, rests at the minimum when
        /// ambient loops are not allowed, and otherwise breathes 0.32-0.72 over 4.6 s (Alternate, SineEaseInOut).
        /// Every 8 s the sheen makes one 0.85 s pass, except while a session runs (the CTA is STOP then).
        /// </summary>
        private void ApplyStartButtonGlow()
        {
            var glow = Named<Border>("StartGlowLayer");
            var host = Named<Grid>("StartSheenHost");
            var band = Named<Border>("StartSheen");
            var slide = (band?.RenderTransform as TransformGroup)?.Children.OfType<TranslateTransform>().FirstOrDefault();
            if (glow is null || host is null || band is null || slide is null) return;
            var tier = Env.CurrentTier;
            bool wanted = Env.AllowGlow(tier) && Env.Level != MotionLevel.Off;
            glow.IsVisible = wanted;
            // WPF's DropShadowEffect (BlurRadius min(22, tier cap), FxGlowColor) as the layer's own BoxShadow:
            // an Effect under a breathing layer is re-blurred every frame (P82).
            glow.BoxShadow = wanted
                ? new BoxShadows(new BoxShadow { Blur = Math.Min(22, Env.MaxGlowBlurRadius(tier)), Color = Env.GlowColor })
                : default;

            bool run = wanted && ChromeAmbientAllowed;
            if (run && StartChromeBreathing) return;   // not running: always re-rest (Off -> Reduced shows the floor)
            _startChromeBeat?.Stop();
            _startChromeBeat = null;
            glow.Opacity = wanted ? StartGlowMinOpacity : 0;
            band.Opacity = 0;
            slide.X = 0;
            if (!run) return;
            _startSheenCycle = -1;
            _startChromeBeat = new BeatLoop(host, t =>
            {
                glow.Opacity = StartGlowMinOpacity
                    + (StartGlowMaxOpacity - StartGlowMinOpacity) * BeatLoop.Breath(t, StartGlowBreathSeconds);
                long cycle = (long)(t / StartSheenIntervalSeconds);
                if (cycle != _startSheenCycle)
                {
                    // A DispatcherTimer's first tick is one interval in; the session check is per pass.
                    _startSheenCycle = cycle;
                    _startSheenThisCycle = cycle > 0 && !CoreEngine.IsRunning;
                }
                double progress = (t - cycle * StartSheenIntervalSeconds) / StartSheenSeconds;
                if (!_startSheenThisCycle || progress >= 1 || host.Bounds.Width <= 0)
                {
                    band.Opacity = 0;
                    slide.X = 0;
                    return;
                }
                SheenAt(host.Bounds.Width, band, slide, progress, 0.30);
            });
            _startChromeBeat.Start();
        }

        /// <summary>WPF ApplyXpSheen (:725): three gradient-stop offsets travel +1.30 over 6 s, repeating;
        /// parked at Opacity 0 with the authored offsets otherwise.</summary>
        private void ApplyXpSheen()
        {
            var sheen = Named<Border>("XPBarSheen");
            if (sheen?.Background is not LinearGradientBrush { GradientStops: { Count: 3 } stops }) return;
            bool run = ChromeAmbientAllowed;
            if (run == XpGlossRunning) return;
            _xpGlossBeat?.Stop();
            _xpGlossBeat = null;
            for (int i = 0; i < 3; i++) stops[i].Offset = XpSheenStarts[i];
            sheen.Opacity = run ? 1 : 0;
            if (!run) return;
            _xpGlossBeat = new BeatLoop(sheen, t =>
            {
                double travel = 1.30 * (t % XpSheenSeconds) / XpSheenSeconds;
                for (int i = 0; i < 3; i++) stops[i].Offset = XpSheenStarts[i] + travel;
            });
            _xpGlossBeat.Start();
        }

        /// <summary>WPF SweepSheen (:607) at <paramref name="progress"/> (0..1): the band travels from
        /// -bandWidth to width + bandWidth/4 on SineEaseInOut and fades 0 -> peak -> peak -> 0.</summary>
        private static void SheenAt(double width, Border band, TranslateTransform slide, double progress, double peak)
        {
            double bandWidth = double.IsNaN(band.Width) || band.Width <= 0 ? 80 : band.Width;
            double eased = (1 - Math.Cos(Math.PI * progress)) / 2;
            slide.X = -bandWidth + (width + bandWidth * 1.25) * eased;
            band.Opacity = BeatLoop.Keys(progress, new[] { (0.0, 0.0), (0.20, peak), (0.75, peak), (1.0, 0.0) });
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
