// WPF MainWindow.ProfileFaucet.cs:290-330, 369-506: waiting-state motion and tick.
using System;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private BeatLoop? _faucetWaitingBeat;
        private readonly RotateTransform _faucetWaitingTilt = new();
        private readonly ScaleTransform _faucetChipScale = new();
        private readonly TranslateTransform _faucetDropFall = new();
        private double _faucetNextTick;
        private static readonly (double At, double V)[] FaucetDropKeys =
            { (0, 0), (.25, .85), (.7, .55), (1, 0) };

        private void UpdateFaucetWaitingMotion(bool waiting)
        {
            var tap = Vat<Grid>("ProfileVatFaucet");
            if (!waiting || !AmbientFxCanvas.Env.AllowAmbientLoops || !_vatOnScreen ||
                tap?.IsEffectivelyVisible != true)
            {
                StopFaucetWaitingMotion();
                return;
            }
            if (_faucetWaitingBeat?.IsRunning == true) return;
            if (Vat<Border>("ProfileVatChip") is { } chip) chip.RenderTransform = _faucetChipScale;
            if (Vat<Ellipse>("ProfileVatFaucetDrop") is { } drop) drop.RenderTransform = _faucetDropFall;
            _faucetNextTick = 3.8;
            _faucetWaitingBeat ??= new BeatLoop(tap, StepFaucetWaitingMotion) { OnParked = StopFaucetWaitingMotion };
            _faucetWaitingBeat.Start();
        }

        private void StepFaucetWaitingMotion(double seconds)
        {
            // Sine easing and auto-reverse, matching the 550 ms rock and 2.8 s breath.
            _faucetWaitingTilt.Angle = -2.2 + 4.4 * BeatLoop.Breath(seconds, .55);
            _faucetChipScale.ScaleX = _faucetChipScale.ScaleY = 1 + .05 * BeatLoop.Breath(seconds, 2.8);
            if ((!_faucetFeedbackActive || _faucetFeedbackPour) && Vat<Ellipse>("ProfileVatFaucetDrop") is { } drop)
            {
                double p = seconds % 1.8 / 1.8;
                drop.Opacity = AmbientFxCanvas.Env.AllowGlow(AmbientFxCanvas.Env.CurrentTier)
                    ? BeatLoop.Keys(p, FaucetDropKeys) : 0;
                _faucetDropFall.Y = 7 * p;
            }
            if (seconds >= _faucetNextTick)
            {
                _faucetNextTick = (Math.Floor(seconds / 3.8) + 1) * 3.8;
                if (VatGlassCanvas.WindowIsPresenting(this)) PlayFaucetSfx("faucet_tick.wav", .15f);
            }
        }

        private void StopFaucetWaitingMotion()
        {
            _faucetWaitingBeat?.Stop();
            _faucetWaitingTilt.Angle = _faucetDropFall.Y = 0;
            _faucetChipScale.ScaleX = _faucetChipScale.ScaleY = 1;
            if (Vat<Ellipse>("ProfileVatFaucetDrop") is { } drop) drop.Opacity = 0;
        }
    }
}
