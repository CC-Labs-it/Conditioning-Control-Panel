using System;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Services.Safety;
namespace ConditioningControlPanel.Avalonia.Views.Windows;
// Part B of the faucet (restack b168, from #4043): the waiting tap's wobble, drip sparkle, sparse tick
// and chip breath (WPF MainWindow.ProfileFaucet.cs:352-489), hover grow and press dip (493-531, 634-638).
// One BeatLoop, plain transform properties, ambient gates; the finite feedback beat keeps the lean and
// the drop while it runs.
public partial class MainShellWindow
{
    private BeatLoop? _faucetAmbientBeat;
    private readonly ScaleTransform _faucetHover = new(), _faucetChipBreath = new();
    private bool _faucetAmbient, _faucetSparkle;
    private long _faucetAmbientAt, _faucetTickAt;
    private (double From, double To, double Ms, long At) _faucetHoverTween = (1, 1, 0, 0);
    private static readonly (double, double)[] FaucetSparkleKeys = { (0, 0), (.25, .85), (.7, .55), (1, 0) };
    internal bool FaucetWobbling => _faucetAmbient;
    /// <summary>The tap's one transform group: lean, shake, hover scale (index order is read by tests).</summary>
    private void EnsureFaucetTransforms(Grid tap)
    {
        if (tap.RenderTransform is TransformGroup g && g.Children.Count == 3 && g.Children[0] == _faucetLean) return;
        var transforms = new TransformGroup();
        transforms.Children.Add(_faucetLean); transforms.Children.Add(_faucetShake); transforms.Children.Add(_faucetHover);
        tap.RenderTransform = transforms;
        if (FaucetControl<Border>("ProfileVatChip") is { } chip) chip.RenderTransform = _faucetChipBreath;
    }
    // StartFaucetWobble / StartFaucetSparkle / StartChipBreath and their Stop twins, idempotent like WPF's flags.
    private void SetFaucetAmbient(bool on)
    {
        _faucetSparkle = on && AmbientFxCanvas.Env.AllowGlow(AmbientFxCanvas.Env.CurrentTier);
        if (on == _faucetAmbient) return;
        _faucetAmbient = on;
        long now = BeatLoop.Time.GetTimestamp();
        _faucetAmbientAt = _faucetTickAt = now;
        if (!FaucetFeedbackRunning) _faucetLean.Angle = 0;
        _faucetChipBreath.ScaleX = _faucetChipBreath.ScaleY = 1;
        if (!on && !FaucetFeedbackRunning && FaucetControl<Ellipse>("ProfileVatFaucetDrop") is { } drop) { drop.Opacity = 0; _faucetDropFall.Y = 0; }
        if (on) KickFaucetAmbient();
    }
    private bool FaucetFeedbackRunning => _faucetFeedbackBeat?.IsRunning == true;
    // Hover 1.16 over 150 ms, press dip .94 over 90 ms, release back to 1 (WPF AnimateFaucetScale).
    private void AnimateFaucetScale(double to, double ms = 150)
    {
        if (!AmbientFxCanvas.Env.AllowTransitions) { _faucetHoverTween = (to, to, 0, 0); _faucetHover.ScaleX = _faucetHover.ScaleY = to; return; }
        _faucetHoverTween = (_faucetHover.ScaleX, to, ms, BeatLoop.Time.GetTimestamp());
        KickFaucetAmbient();
    }
    private void KickFaucetAmbient()
    {
        if (FaucetControl<Grid>("ProfileVatFaucet") is not { } tap) return;
        EnsureFaucetTransforms(tap);
        _faucetAmbientBeat ??= new BeatLoop(tap, _ => StepFaucetAmbient());
        _faucetAmbientBeat.Start();
    }
    internal void StepFaucetAmbient()
    {
        long now = BeatLoop.Time.GetTimestamp();
        double Since(long at) => BeatLoop.Time.GetElapsedTime(at, now).TotalMilliseconds;
        var tw = _faucetHoverTween;
        double p = tw.Ms <= 0 ? 1 : Math.Clamp(Since(tw.At) / tw.Ms, 0, 1);
        _faucetHover.ScaleX = _faucetHover.ScaleY = tw.From + (tw.To - tw.From) * (1 - (1 - p) * (1 - p));
        bool tweening = p < 1;
        if (_faucetAmbient)
        {
            double t = Since(_faucetAmbientAt) / 1000;
            _faucetChipBreath.ScaleX = _faucetChipBreath.ScaleY = 1 + .05 * BeatLoop.Breath(t, 2.8);
            if (!FaucetFeedbackRunning)
            {
                _faucetLean.Angle = -2.2 + 4.4 * BeatLoop.Breath(t, .55);
                if (_faucetSparkle && FaucetControl<Ellipse>("ProfileVatFaucetDrop") is { } drop)
                {
                    double s = t * 1000 % 1800 / 1800;
                    drop.Opacity = Math.Clamp(BeatLoop.Keys(s, FaucetSparkleKeys), 0, 1);
                    _faucetDropFall.Y = 7 * s;
                }
            }
            // The tick is SPARSE: one soft tick every 3.8 s, only while the window is in front of somebody.
            if (Since(_faucetTickAt) >= 3800)
            {
                _faucetTickAt = now;
                if (VatGlassCanvas.WindowIsPresenting(this)) PlayFaucetSfx("faucet_tick.wav", .15f);
            }
        }
        if (!_faucetAmbient && !tweening) _faucetAmbientBeat?.Stop();
    }
    /// <summary>DisarmFaucet: the ambient clock parked and every ambient transform at rest.</summary>
    private void StopFaucetAmbient()
    {
        SetFaucetAmbient(false);
        _faucetAmbientBeat?.Stop();
        _faucetHoverTween = (1, 1, 0, 0);
        _faucetHover.ScaleX = _faucetHover.ScaleY = 1;
    }
    /// <summary>WPF MainWindow.ProfileFaucet.cs:760-783: the charge ring fades in over 120 ms (Gentle: the rise floor).</summary>
    private static double FaucetRingOpacity(double ms) =>
        AmbientFxCanvas.Env.AllowTransitions ? Math.Clamp(ms / GentleVisuals.RiseMs(120), 0, 1) : 1;
}
