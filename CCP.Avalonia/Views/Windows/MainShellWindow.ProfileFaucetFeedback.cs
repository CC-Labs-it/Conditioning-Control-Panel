// WPF MainWindow.ProfileFaucet.cs:510-547, 636, 694-767, 871-1078.
using System;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private readonly ScaleTransform _faucetScale = new(), _faucetStamp = new();
    private readonly RotateTransform _faucetLean = new();
    private readonly TranslateTransform _faucetShake = new();
    private BeatLoop? _faucetScaleBeat, _faucetFeedbackBeat;
    private double _scaleFrom, _scaleTo, _scaleDuration, _settleAt, _nextSettle;
    private bool _faucetFeedbackActive, _faucetFeedbackPour;

    private void AttachFaucetTransforms(Grid tap)
    {
        var group = new TransformGroup();
        group.Children.Add(_faucetShake);
        group.Children.Add(_faucetScale);
        group.Children.Add(_faucetWaitingTilt);
        group.Children.Add(_faucetLean);
        tap.RenderTransform = group;
        if (Vat<Grid>("ProfileVatHandle") is { } handle) handle.RenderTransform = _faucetStamp;
        if (Vat<Ellipse>("ProfileVatFaucetDrop") is { } drop) drop.RenderTransform = _faucetDropFall;
    }

    private void AnimateFaucetScale(double target, double seconds = .15)
    {
        _faucetScaleBeat?.Stop();
        if (!AmbientFxCanvas.Env.AllowTransitions || Vat<Grid>("ProfileVatFaucet") is not { } tap)
        {
            _faucetScale.ScaleX = _faucetScale.ScaleY = target;
            return;
        }
        _scaleFrom = _faucetScale.ScaleX; _scaleTo = target; _scaleDuration = seconds;
        _faucetScaleBeat ??= new BeatLoop(tap, StepFaucetScale) { OnParked = StopFaucetScale };
        _faucetScaleBeat.Start();
    }

    private void StepFaucetScale(double seconds)
    {
        double t = Math.Clamp(seconds / _scaleDuration, 0, 1);
        _faucetScale.ScaleX = _faucetScale.ScaleY = _scaleFrom + (_scaleTo - _scaleFrom) * (1 - (1 - t) * (1 - t));
        if (t >= 1) _faucetScaleBeat?.Stop();
    }

    private void StopFaucetScale()
    {
        _faucetScaleBeat?.Stop();
        _faucetScale.ScaleX = _faucetScale.ScaleY = 1;
    }

    private void StartFaucetFeedback(bool pour)
    {
        StopFaucetFeedback();
        if (!pour && !AmbientFxCanvas.Env.AllowTransitions) return;
        if (Vat<Grid>("ProfileVatFaucet") is not { } tap) return;
        _faucetFeedbackActive = true; _faucetFeedbackPour = pour;
        _settleAt = double.PositiveInfinity; _nextSettle = .25;
        if (pour && !_faucetChargeCompact && AmbientFxCanvas.Env.Level == MotionLevel.Full && Vat<Canvas>("ProfileVatSparks") is { } host)
            for (int i = 0, count = Random.Shared.Next(5, 10); i < count; i++)
            {
                double a = Math.PI * 2 * i / count + Random.Shared.NextDouble() * .5;
                double dist = 12 + Random.Shared.NextDouble() * 16, size = 2.2 + Random.Shared.NextDouble() * 2;
                var mote = new Ellipse { Width = size, Height = size, Fill = tap.TryFindResource("PinkBrush", out var brush) && brush is IBrush pink ? pink : Brushes.HotPink, IsHitTestVisible = false, RenderTransform = new TranslateTransform(), Tag = (Math.Cos(a) * dist, Math.Sin(a) * dist + 6, .42 + Random.Shared.NextDouble() * .2) };
                Canvas.SetLeft(mote, tap.Margin.Left + 27 - size / 2);
                Canvas.SetTop(mote, tap.Margin.Top + 22 - size / 2);
                host.Children.Add(mote);
            }
        _faucetFeedbackBeat ??= new BeatLoop(tap, StepFaucetFeedback) { OnParked = StopFaucetFeedback };
        _faucetFeedbackBeat.Start();
    }

    private void StepFaucetFeedback(double seconds)
    {
        if (!_vatOnScreen) { StopFaucetFeedback(); return; }
        bool motion = AmbientFxCanvas.Env.AllowTransitions;
        if (_faucetFeedbackPour)
        {
            if (seconds >= _nextSettle)
            {
                _nextSettle = seconds + .25;
                if (VatGlass?.IsPouring != true && double.IsPositiveInfinity(_settleAt))
                {
                    _settleAt = seconds;
                    UpdateFaucetPresentation();
                }
            }
            double t = Math.Clamp(seconds / .34, 0, 1), u = Math.Min(1, t / .56);
            double stamp = t < .56 ? 2.1 + (.86 - 2.1) * (1 - Math.Pow(1 - u, 3)) : .86 + .14 * (t < .78 ? 4 * Math.Pow((t - .56) / .44, 3) : 1 - Math.Pow(-2 * (t - .56) / .44 + 2, 3) / 2);
            _faucetStamp.ScaleX = _faucetStamp.ScaleY = motion ? stamp : 1;
            _faucetLean.Angle = !motion ? 0 : double.IsPositiveInfinity(_settleAt) ? 14 * (1 - Math.Pow(1 - Math.Clamp(seconds / .24, 0, 1), 2)) : 14 * Math.Pow(1 - Math.Clamp((seconds - _settleAt) / .24, 0, 1), 2);
            if (Vat<Canvas>("ProfileVatSparks") is { } host)
                for (int i = host.Children.Count - 1; i >= 0; i--)
                {
                    var mote = (Ellipse)host.Children[i]; var (x, y, duration) = ((double, double, double))mote.Tag!;
                    double p = motion ? Math.Clamp(seconds / duration, 0, 1) : 1, ease = 1 - (1 - p) * (1 - p);
                    ((TranslateTransform)mote.RenderTransform!).X = x * ease; ((TranslateTransform)mote.RenderTransform!).Y = y * ease; mote.Opacity = 1 - p;
                    if (p >= 1) host.Children.RemoveAt(i);
                }
            if (seconds >= .34 && seconds >= _settleAt + (motion ? .24 : 0) && (Vat<Canvas>("ProfileVatSparks")?.Children.Count ?? 0) == 0)
            {
                StopFaucetFeedback();
            }
        }
        else
        {
            if (!motion) { StopFaucetFeedback(); return; }
            double[] amps = { 0, -2.6, 2.2, -1.6, 1.1, -.6, .25, 0 };
            double p = Math.Clamp(seconds / .42, 0, 1) * 7; int i = Math.Min(6, (int)p);
            _faucetShake.X = amps[i] + (amps[i + 1] - amps[i]) * (p - i);
            double d = Math.Clamp(seconds / .52, 0, 1); _faucetDropFall.Y = 10 * d * d;
            if (Vat<Ellipse>("ProfileVatFaucetDrop") is { } drop) drop.Opacity = d < .2 ? .95 * d / .2 : .95 * (1 - d) / .8;
            if (seconds >= .52) StopFaucetFeedback();
        }
    }

    private void StopFaucetFeedback()
    {
        _faucetFeedbackBeat?.Stop(); _faucetFeedbackActive = false;
        _faucetStamp.ScaleX = _faucetStamp.ScaleY = 1;
        _faucetLean.Angle = _faucetShake.X = 0;
        if (_faucetWaitingBeat?.IsRunning != true)
        {
            _faucetDropFall.Y = 0;
            if (Vat<Ellipse>("ProfileVatFaucetDrop") is { } drop) drop.Opacity = 0;
        }
        Vat<Canvas>("ProfileVatSparks")?.Children.Clear();
    }
}
