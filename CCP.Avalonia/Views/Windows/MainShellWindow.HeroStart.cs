// PORTED from ConditioningControlPanel/MainWindow/MainWindow.HeroFx.cs sections 1-3 and 6: the
// START button's charged idle (ApplyStartCharge/TintStartCharge, :201-324), ring exhale
// (:326-412), ignition (FlashStartIgnition, :414-501) and running heartbeat (:504-553), plus the
// Save button's FlashSaveAbsorb (:702-757). Callers as WPF: UpdateStartButton /
// UpdateStartButtonForRemoteControl tails (StartStop.cs:989,1049), BtnStart_Click's first line
// (StartStop.cs:40), BtnSave_Click's first line (Settings.cs:381) and the mod-switch sweep
// (UiUpdates.cs:275). Loops ride the shared beat (BeatLoop) instead of WPF clocks; ambient ones
// ask HeroAmbient (WPF ChromeAmbientAllowed), one-shots ask AllowTransitions.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private const double StartChargeDriftSeconds = 4.5, StartExhaleCycleSeconds = 4.2, StartExhaleTravelSeconds = 1.35;
        private const double StartCtaHeight = 50, StartCtaFallbackWidth = 320;

        private LinearGradientBrush? _startChargeBrush;
        private bool _startChargeApplied, _startDipInFlight, _startHeroHooked;
        private BeatLoop? _startDrift, _startExhale, _startHeartbeat, _startIgnition, _saveAbsorb;

        internal bool StartChargeApplied => _startChargeApplied;
        internal bool StartChargeDrifting => _startDrift?.IsRunning == true;
        internal bool StartExhaling => _startExhale?.IsRunning == true;
        internal bool StartHeartbeating => _startHeartbeat?.IsRunning == true;
        internal bool StartIgniting => _startIgnition?.IsRunning == true;
        internal bool SaveAbsorbing => _saveAbsorb?.IsRunning == true;

        /// <summary>WPF ChromeAmbientAllowed: window focused, shown, not minimised, ambient loops allowed.</summary>
        private bool HeroAmbient => !_heroClosed && AmbientFxCanvas.Env.AllowAmbientLoops && IsActive
            && IsVisible && WindowState != WindowState.Minimized;

        private void HookHeroStartFx()
        {
            if (_startHeroHooked) return;
            _startHeroHooked = true;
            EventHandler<ModPackage> modChanged = (_, _) => Dispatcher.UIThread.Post(TintStartCharge);
            CoreMods.ModChanged += modChanged;
            Closed += (_, _) =>
            {
                CoreMods.ModChanged -= modChanged;
                _startDrift?.Stop();
                _startExhale?.Stop();
                _startHeartbeat?.Stop();
                _startIgnition?.Stop();
                _saveAbsorb?.Stop();
            };
        }

        /// <summary>WPF ApplyStartHeroState: idle gets the charge + exhale; running gets the heartbeat;
        /// remote/disabled gets neither. Only ever releases a background it painted itself.</summary>
        internal void ApplyStartHeroState()
        {
            try
            {
                if (Named<Button>("BtnStart") is not { } b) return;
                bool remote = RemoteControllerConnected, running = CoreEngine.IsRunning;
                if (running || remote || !b.IsEnabled)
                {
                    ReleaseStartCharge(b);
                    ParkRing(Named<Border>("StartRingExhale"), ref _startExhale);
                    ApplyStartHeartbeat(running && !remote);
                    return;
                }
                ApplyStartHeartbeat(false);
                ApplyStartCharge(b);
                ApplyStartRingExhale();
            }
            catch (Exception ex) { Log.Debug("ApplyStartHeroState: {E}", ex.Message); }
        }

        private void ApplyStartCharge(Button b)
        {
            // Slightly off-horizontal, as WPF: a leaning gradient reads as light on a surface.
            var brush = _startChargeBrush ??= new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0.35, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Colors.Transparent, 0), new GradientStop(Colors.Transparent, 0.5), new GradientStop(Colors.Transparent, 1) },
            };
            TintStartCharge();
            if (!ReferenceEquals(b.Background, brush)) b.Bind(BackgroundProperty, new Binding { Source = brush });
            _startChargeApplied = true;

            var stops = brush.GradientStops;
            if (!HeroAmbient)
            {
                _startDrift?.Stop();
                (stops[0].Offset, stops[1].Offset, stops[2].Offset) = (0.0, 0.5, 1.0);
                return;
            }
            // Three 4.5 s legs, AutoReverse + SineEaseInOut (a 9 s round trip).
            _startDrift ??= new BeatLoop(b, t =>
            {
                double k = BeatLoop.Breath(t, StartChargeDriftSeconds);
                stops[0].Offset = -0.10 + 0.20 * k;
                stops[1].Offset = 0.34 + 0.32 * k;
                stops[2].Offset = 0.90 + 0.20 * k;
            });
            _startDrift.Start();
        }

        private void ReleaseStartCharge(Button b)
        {
            _startDrift?.Stop();
            if (!_startChargeApplied) return;
            _startChargeApplied = false;
            // The red STOP / green REMOTE painted by the caller stands; only our own write is undone.
            if (ReferenceEquals(b.Background, _startChargeBrush))
                b.Bind(BackgroundProperty, b.GetResourceObservable("AccentGradientBrush"));
        }

        /// <summary>WPF TintStartCharge: the glow colour, rotated -27 deg (violet) and +38 deg (coral), so
        /// the default #FF69B4 lands on FF69B4 / E85CE0 / FF7E6B and other mods get their own family.</summary>
        internal void TintStartCharge()
        {
            if (_startChargeBrush is not { } brush) return;
            try
            {
                var hsv = AmbientFxCanvas.Env.GlowColor.ToHsv();
                double h = hsv.H, s = Math.Max(hsv.S, 0.45), v = hsv.V;
                brush.GradientStops[0].Color = Hsv(h, s, v);
                brush.GradientStops[1].Color = Hsv(h - 27, Math.Min(1, s + 0.02), v * 0.91);
                brush.GradientStops[2].Color = Hsv(h + 38, s, v);
            }
            catch (Exception ex) { Log.Debug("TintStartCharge: {E}", ex.Message); }
        }

        private static Color Hsv(double h, double s, double v)
            => HsvColor.ToRgb(((h % 360) + 360) % 360, Math.Clamp(s, 0, 1), Math.Clamp(v, 0, 1), 1);

        /// <summary>WPF ApplyStartRingExhale: a 2px ring swells 5px out and fades over 1.35 s, every 4.2 s.</summary>
        private void ApplyStartRingExhale()
        {
            if (Named<Border>("StartRingExhale") is not { RenderTransform: ScaleTransform scale } ring) return;
            if (!HeroAmbient) { ParkRing(ring, ref _startExhale); return; }
            if (_startExhale?.IsRunning == true) return;
            _startExhale = new BeatLoop(ring, t =>
            {
                double c = t % StartExhaleCycleSeconds, travel = StartExhaleTravelSeconds;
                ring.Opacity = BeatLoop.Keys(c, new[] { (0.0, 0.0), (travel * 0.35, 0.70), (travel, 0.0) });
                double p = c < travel ? 1 - (1 - c / travel) * (1 - c / travel) : c < travel + 0.05 ? 1 : 0;
                scale.ScaleX = 1 + (GrowFactor(StartCtaWidth, 5) - 1) * p;
                scale.ScaleY = 1 + (GrowFactor(StartCtaHeight, 5) - 1) * p;
            });
            _startExhale.Start();
        }

        private static void ParkRing(Border? ring, ref BeatLoop? loop)
        {
            loop?.Stop();
            if (ring == null) return;
            ring.Opacity = 0;
            if (ring.RenderTransform is ScaleTransform scale) scale.ScaleX = scale.ScaleY = 1;
        }

        /// <summary>WPF ApplyStartHeartbeat: 0.6px of swell on a 2.4 s clock while running; never
        /// yanks the transform out from under a press dip (its completion re-applies the state).</summary>
        private void ApplyStartHeartbeat(bool wanted)
        {
            if (_startDipInFlight || Named<Button>("BtnStart") is not { RenderTransform: ScaleTransform rig } b) return;
            if (!wanted || !HeroAmbient)
            {
                _startHeartbeat?.Stop();
                rig.ScaleX = rig.ScaleY = 1;
                return;
            }
            if (_startHeartbeat?.IsRunning == true) return;
            _startHeartbeat = new BeatLoop(b, t =>
            {
                double k = BeatLoop.Breath(t, 2.4);
                rig.ScaleX = 1 + (GrowFactor(StartCtaWidth, 0.6) - 1) * k;
                rig.ScaleY = 1 + (GrowFactor(StartCtaHeight, 0.6) - 1) * k;
            });
            _startHeartbeat.Start();
        }

        /// <summary>WPF FlashStartIgnition: a 100 ms 0.97 dip, a 300 ms white wash, and two rings thrown
        /// 8px / 20px off the edge (550 / 750 ms, 80 ms apart). Pure decoration; reads no state.</summary>
        internal void FlashStartIgnition()
        {
            try
            {
                if (_heroClosed || !AmbientFxCanvas.Env.AllowTransitions || Named<Button>("BtnStart") is not { } b) return;
                var press = b.RenderTransform as ScaleTransform;
                var flash = Named<Border>("StartIgnitionFlash");
                var ringA = Named<Border>("StartRingBurstA");
                var ringB = Named<Border>("StartRingBurstB");
                double width = StartCtaWidth;
                _startIgnition?.Stop();
                if (press != null)
                {
                    _startHeartbeat?.Stop();   // WPF: the dip's SnapshotAndReplace takes the transform over
                    _startDipInFlight = true;
                }
                _startIgnition = new BeatLoop(b, t =>
                {
                    if (press != null && _startDipInFlight)
                    {
                        double dip = t < 0.04 ? 1 - 0.03 * QuadOut(t / 0.04) : 0.97 + 0.03 * QuadOut(Math.Min(1, (t - 0.04) / 0.06));
                        press.ScaleX = press.ScaleY = dip;
                        if (t >= 0.1) { _startDipInFlight = false; ApplyStartHeroState(); }
                    }
                    if (flash != null) flash.Opacity = 0.55 * (1 - QuadOut(Math.Min(1, t / 0.3)));
                    BurstRing(ringA, t, 0, 0.55, GrowFactor(width, 8), GrowFactor(StartCtaHeight, 8));
                    BurstRing(ringB, t, 0.08, 0.75, GrowFactor(width, 20), GrowFactor(StartCtaHeight, 20));
                    if (t >= 0.83) EndStartIgnition();
                });
                _startIgnition.Start();
            }
            catch (Exception ex) { Log.Debug("FlashStartIgnition: {E}", ex.Message); }
        }

        private static void BurstRing(Border? ring, double t, double delay, double span, double toX, double toY)
        {
            if (ring?.RenderTransform is not ScaleTransform scale) return;
            double p = Math.Clamp((t - delay) / span, 0, 1), e = QuadOut(p);
            bool live = t >= delay && p < 1;
            ring.Opacity = live ? 0.95 * (1 - e) : 0;
            scale.ScaleX = live ? 1 + (toX - 1) * e : 1;
            scale.ScaleY = live ? 1 + (toY - 1) * e : 1;
        }

        /// <summary>Ends the ignition at its rest frame (completion, or the motion gate closing mid-flight).</summary>
        private void EndStartIgnition()
        {
            bool wasRunning = _startIgnition?.IsRunning == true;
            _startIgnition?.Stop();
            if (!wasRunning) return;
            if (Named<Border>("StartIgnitionFlash") is { } flash) flash.Opacity = 0;
            BurstRing(Named<Border>("StartRingBurstA"), 1, 0, 0.55, 1, 1);
            BurstRing(Named<Border>("StartRingBurstB"), 1, 0, 0.75, 1, 1);
            if (_startDipInFlight)
            {
                _startDipInFlight = false;
                if (Named<Button>("BtnStart")?.RenderTransform is ScaleTransform press) press.ScaleX = press.ScaleY = 1;
                ApplyStartHeroState();
            }
        }

        /// <summary>WPF FlashSaveAbsorb: a tick that draws itself (400 ms), holds 1.6 s and fades 260 ms,
        /// plus one ripple 14px off the Save button's edge (520 ms). The save itself is untouched.</summary>
        internal void FlashSaveAbsorb()
        {
            try
            {
                if (_heroClosed || !AmbientFxCanvas.Env.AllowTransitions || Named<Grid>("SaveAbsorbHost") is not { } host) return;
                var tick = Named<Path>("SaveTick");
                var ripple = Named<Border>("SaveRipple");
                double width = host.Bounds.Width;
                _saveAbsorb?.Stop();
                _saveAbsorb = new BeatLoop(host, t =>
                {
                    if (tick != null)
                    {
                        tick.StrokeDashOffset = 9 * (1 - QuadOut(Math.Min(1, t / 0.4)));
                        tick.Opacity = BeatLoop.Keys(t, new[] { (0.0, 1.0), (1.6, 1.0), (1.86, 0.0) });
                    }
                    if (ripple?.RenderTransform is ScaleTransform scale)
                    {
                        double e = QuadOut(Math.Min(1, t / 0.52));
                        ripple.Opacity = 0.75 * (1 - e);
                        scale.ScaleX = 1 + (GrowFactor(width, 14) - 1) * e;
                        scale.ScaleY = 1 + (GrowFactor(StartCtaHeight, 14) - 1) * e;
                    }
                    if (t >= 1.86) _saveAbsorb?.Stop();
                });
                _saveAbsorb.Start();
            }
            catch (Exception ex) { Log.Debug("FlashSaveAbsorb: {E}", ex.Message); }
        }

        private void EndSaveAbsorb()
        {
            if (_saveAbsorb?.IsRunning != true) return;
            _saveAbsorb.Stop();
            if (Named<Path>("SaveTick") is { } tick) tick.Opacity = 0;
            if (Named<Border>("SaveRipple") is { } ripple) ripple.Opacity = 0;
        }

        /// <summary>Measured CTA width, with WPF's 320px fallback before the first layout pass.</summary>
        private double StartCtaWidth
        {
            get
            {
                double w = Named<Grid>("StartRingHost")?.Bounds.Width ?? 0;
                if (w <= 1) w = Named<Button>("BtnStart")?.Bounds.Width ?? 0;
                return w > 1 ? w : StartCtaFallbackWidth;
            }
        }

        private static double GrowFactor(double size, double growPx) => size <= 1 ? 1.0 : (size + growPx) / size;

        private static double QuadOut(double p) => 1 - (1 - p) * (1 - p);
    }
}
