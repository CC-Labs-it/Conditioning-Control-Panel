// The faucet's motion, from WPF MainWindow.ProfileFaucet.cs: wobble, sparkle, tick and chip breath
// (352-489), hover (493-531), ring fade-in (760-783), thud, shiver, drip, sparks and tilt (852-1022) and
// the settle watch (1030-1059). Everything rides ONE BeatLoop on the window's shared 30 fps beat and
// writes plain transform properties (P82: no infinite Animation, no Animation on a Transform).
using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Models;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // WPF names these ProfileVatFaucetShake/Scale/Tilt, ProfileVatHandleScale, ProfileVatFaucetDropFall
        // and ProfileVatChipScale; an Avalonia Transform cannot carry x:Name, so WireFaucet attaches them.
        internal readonly ScaleTransform FaucetScale = new(), FaucetHandleScale = new(), FaucetChipScale = new();
        internal readonly RotateTransform FaucetTilt = new();
        internal readonly TranslateTransform FaucetShake = new(), FaucetDropFall = new();
        private readonly List<(Ellipse Mote, TranslateTransform Move, double Dx, double Dy, double Ms, long At)> _faucetMotes = new();
        private static readonly Random FaucetRng = new();
        private static readonly (double, double)[] ShiverKeys = { (0, 0), (60, -2.6), (120, 2.2), (180, -1.6), (240, 1.1), (300, -.6), (360, .25), (420, 0) };
        private static readonly (double, double)[] SparkleKeys = { (0, 0), (.25, .85), (.7, .55), (1, 0) };
        private static readonly (double, double)[] DripKeys = { (0, 0), (.2, .95), (1, 0) };
        private BeatLoop? _faucetFx;
        private (double From, double To, double Ms, long At) _faucetScaleTw = (1, 1, 0, 0), _faucetTiltTw;
        private long? _wobbleAt, _chipBreathAt, _thudAt, _shiverAt, _dripAt;
        private long _lastFaucetTick;
        private bool _faucetSparkling, _faucetSettling;

        internal bool FaucetWobbling => _wobbleAt != null;

        private static long FxNow => BeatLoop.Time.GetTimestamp();

        /// <summary>StartFaucetWobble/StartChipBreath and their Stop twins: idempotent, like WPF's flags.</summary>
        private void SetFaucetAmbient(bool on)
        {
            if (on != (_wobbleAt != null))
            {
                _wobbleAt = on ? FxNow : null;
                _lastFaucetTick = FxNow;
                FaucetTilt.Angle = 0;
                _faucetTiltTw = default;
            }
            if (on != (_chipBreathAt != null)) { _chipBreathAt = on ? FxNow : null; FaucetChipScale.ScaleX = FaucetChipScale.ScaleY = 1; }
            _faucetSparkling = on && Env.AllowGlow(Env.CurrentTier);
            if (on) KickFaucetFx();
            else if (_dripAt == null && Vat<Ellipse>("ProfileVatFaucetDrop") is { } drop) { drop.Opacity = 0; FaucetDropFall.Y = 0; }
        }

        private void AnimateFaucetScale(double to, double ms = 150)
        {
            if (!Env.AllowTransitions) { _faucetScaleTw = (to, to, 0, 0); FaucetScale.ScaleX = FaucetScale.ScaleY = to; return; }
            _faucetScaleTw = (FaucetScale.ScaleX, to, ms, FxNow);
            KickFaucetFx();
        }

        private void AnimateFaucetTilt(double angle)
        {
            if (!Env.AllowTransitions) { _faucetTiltTw = default; FaucetTilt.Angle = 0; return; } // reduced: the tap never leans
            _faucetTiltTw = (FaucetTilt.Angle, angle, 240, FxNow);
            KickFaucetFx();
        }

        private void FaucetThud()
        {
            if (!Env.AllowTransitions) { _thudAt = null; FaucetHandleScale.ScaleX = FaucetHandleScale.ScaleY = 1; return; }
            _thudAt = FxNow;
            KickFaucetFx();
        }

        /// <summary>Shiver + drip on an early release (WPF FaucetShiver / FaucetDrip).</summary>
        private void FaucetShiverAndDrip()
        {
            if (!Env.AllowTransitions) { _shiverAt = null; FaucetShake.X = 0; return; }
            _shiverAt = _dripAt = FxNow;
            KickFaucetFx();
        }

        /// <summary>5-9 motes from the spout, ~600 ms; Full motion only (a press is not an ambient field).</summary>
        private void SpawnFaucetSparkles()
        {
            if (Env.Level != MotionLevel.Full || Vat<Canvas>("ProfileVatSparks") is not { } host || Vat<Grid>("ProfileVatFaucet") is not { } tap) return;
            double ox = tap.Margin.Left + 27, oy = tap.Margin.Top + 22; // FaucetSpoutXInBox / YInBox
            IBrush brush = this.TryFindResource("PinkBrush", out var b) && b is IBrush pink ? pink : Brushes.HotPink;
            int count = FaucetRng.Next(5, 10);
            for (int i = 0; i < count; i++)
            {
                double angle = Math.PI * 2 * i / count + FaucetRng.NextDouble() * .5, dist = 12 + FaucetRng.NextDouble() * 16, size = 2.2 + FaucetRng.NextDouble() * 2;
                var move = new TranslateTransform();
                var mote = new Ellipse { Width = size, Height = size, Fill = brush, IsHitTestVisible = false, RenderTransform = move };
                Canvas.SetLeft(mote, ox - size / 2);
                Canvas.SetTop(mote, oy - size / 2);
                host.Children.Add(mote);
                _faucetMotes.Add((mote, move, Math.Cos(angle) * dist, Math.Sin(angle) * dist + 6, 420 + FaucetRng.Next(200), FxNow));
            }
            KickFaucetFx();
        }

        /// <summary>Right the tap once the stream stops (a mid-pour delta can extend it, so no fixed timer).</summary>
        private void StartFaucetSettleWatch() { _faucetSettling = true; KickFaucetFx(); }

        private void KickFaucetFx()
        {
            if (Vat<Grid>("ProfileVatFaucet") is not { } tap) return;
            _faucetFx ??= new BeatLoop(tap, _ => StepFaucetFx());
            _faucetFx.Start();
        }

        /// <summary>DisarmFaucet: every clock parked, every transform at rest.</summary>
        private void StopFaucetFx()
        {
            _faucetFx?.Stop();
            SetFaucetAmbient(false);
            _faucetSettling = false;
            _thudAt = _shiverAt = _dripAt = null;
            _faucetScaleTw = (1, 1, 0, 0);
            _faucetTiltTw = default;
            FaucetScale.ScaleX = FaucetScale.ScaleY = FaucetHandleScale.ScaleX = FaucetHandleScale.ScaleY = 1;
            FaucetShake.X = FaucetTilt.Angle = 0;
            foreach (var m in _faucetMotes) (m.Mote.Parent as Canvas)?.Children.Remove(m.Mote);
            _faucetMotes.Clear();
        }

        internal void StepFaucetFx()
        {
            long now = FxNow;
            bool busy = false;
            double Since(long at) => BeatLoop.Time.GetElapsedTime(at, now).TotalMilliseconds;
            static double QuadOut(double p) => 1 - (1 - p) * (1 - p);
            double Tween((double From, double To, double Ms, long At) tw)
            {
                double p = tw.Ms <= 0 ? 1 : Math.Clamp(Since(tw.At) / tw.Ms, 0, 1);
                busy |= p < 1;
                return tw.From + (tw.To - tw.From) * QuadOut(p);
            }
            double? Once(ref long? at, double ms)
            {
                if (at is not { } a) return null;
                double p = Math.Max(0, Since(a) / ms);
                if (p >= 1) { at = null; return null; }
                busy = true;
                return p;
            }

            if (_faucetSettling)
            {
                busy = true;
                if (VatGlass?.IsPouring != true) { _faucetSettling = false; AnimateFaucetTilt(0); UpdateFaucetPresentation(); }
            }

            FaucetScale.ScaleX = FaucetScale.ScaleY = Tween(_faucetScaleTw);
            FaucetTilt.Angle = _wobbleAt is { } w ? -2.2 + 4.4 * BeatLoop.Breath(Since(w) / 1000, .55) : Tween(_faucetTiltTw);
            FaucetShake.X = Once(ref _shiverAt, 420) is { } sp ? BeatLoop.Keys(sp * 420, ShiverKeys) : 0;
            // THE THUD: 2.1 -> 0.86 (cubic out, 56%) -> 1.0 (cubic in-out) over 340 ms.
            double handle = 1;
            if (Once(ref _thudAt, 340) is { } tp)
            {
                double q = tp < .56 ? tp / .56 : (tp - .56) / .44;
                handle = tp < .56 ? 2.1 - 1.24 * (1 - Math.Pow(1 - q, 3)) : .86 + .14 * (q < .5 ? 4 * q * q * q : 1 - Math.Pow(2 - 2 * q, 3) / 2);
            }
            FaucetHandleScale.ScaleX = FaucetHandleScale.ScaleY = handle;
            FaucetChipScale.ScaleX = FaucetChipScale.ScaleY = _chipBreathAt is { } c ? 1 + .05 * BeatLoop.Breath(Since(c) / 1000, 2.8) : 1;

            // WPF starts the sparkle right after a drip, replacing it, so the sparkle wins here too.
            double drop = 0, fall = 0;
            double? dp = Once(ref _dripAt, 520);
            if (_faucetSparkling && _wobbleAt is { } s) { double p = Since(s) % 1800 / 1800; drop = BeatLoop.Keys(p, SparkleKeys); fall = 7 * p; }
            else if (dp is { } d) { drop = BeatLoop.Keys(d, DripKeys); fall = 10 * d * d; }
            if (Vat<Ellipse>("ProfileVatFaucetDrop") is { } dropDot) dropDot.Opacity = Math.Clamp(drop, 0, 1);
            FaucetDropFall.Y = fall;

            for (int i = _faucetMotes.Count - 1; i >= 0; i--)
            {
                var m = _faucetMotes[i];
                double p = Since(m.At) / m.Ms;
                if (p >= 1) { (m.Mote.Parent as Canvas)?.Children.Remove(m.Mote); _faucetMotes.RemoveAt(i); continue; }
                busy = true;
                m.Move.X = m.Dx * QuadOut(p);
                m.Move.Y = m.Dy * QuadOut(p);
                m.Mote.Opacity = Math.Clamp(1 - p, 0, 1);
            }

            // The tick is SPARSE: one soft tick every 3.8 s, and only while the window is in front of somebody.
            if (_wobbleAt != null && Since(_lastFaucetTick) >= 3800)
            {
                _lastFaucetTick = now;
                if (VatGlassCanvas.WindowIsPresenting(this)) PlayFaucetSfx("faucet_tick.wav", .15f);
            }

            if (!busy && _wobbleAt == null && _chipBreathAt == null) _faucetFx?.Stop();
        }
    }
}
