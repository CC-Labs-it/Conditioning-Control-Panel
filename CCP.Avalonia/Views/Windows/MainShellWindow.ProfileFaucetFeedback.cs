using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Haptics.Core;
namespace ConditioningControlPanel.Avalonia.Views.Windows;
public partial class MainShellWindow
{
    private readonly Stopwatch _faucetFeedbackClock = new();
    private DispatcherTimer? _faucetFeedbackTimer;
    private readonly ScaleTransform _faucetStamp = new();
    private readonly RotateTransform _faucetLean = new();
    private readonly TranslateTransform _faucetShake = new(), _faucetDropFall = new();
    private bool _faucetFeedbackPour;
    private double _faucetSettleAt, _faucetNextSettle;
    // WPF MainWindow.ProfileFaucet.cs:871,906,936,969,1022,1049. Finite gesture FX ignore ambient caps.
    private void StartFaucetFeedback(bool pour)
    {
        StopFaucetFeedback();
        if (!AmbientFxCanvas.Env.AllowTransitions) return;
        _faucetFeedbackPour = pour; _faucetSettleAt = double.PositiveInfinity; _faucetNextSettle = 250;
        var tap = FaucetControl<Grid>("ProfileVatFaucet")!;
        var transforms = new TransformGroup(); transforms.Children.Add(_faucetLean); transforms.Children.Add(_faucetShake); tap.RenderTransform = transforms;
        FaucetControl<Grid>("ProfileVatHandle")!.RenderTransform = _faucetStamp;
        FaucetControl<Ellipse>("ProfileVatFaucetDrop")!.RenderTransform = _faucetDropFall;
        if (pour && _faucetPours < 3 && AmbientFxCanvas.Env.Level == MotionLevel.Full && FaucetControl<Canvas>("ProfileVatSparks") is { } host)
            for (int i = 0, count = Random.Shared.Next(5, 10); i < count; i++)
            {
                double a = Math.PI * 2 * i / count + Random.Shared.NextDouble() * .5, dist = 12 + Random.Shared.NextDouble() * 16, size = 2.2 + Random.Shared.NextDouble() * 2;
                var mote = new Ellipse { Width = size, Height = size, Fill = tap.TryFindResource("PinkBrush", out var brush) && brush is IBrush pink ? pink : Brushes.HotPink, IsHitTestVisible = false, RenderTransform = new TranslateTransform(), Tag = (Math.Cos(a) * dist, Math.Sin(a) * dist + 6, 420.0 + Random.Shared.Next(200)) };
                Canvas.SetLeft(mote, tap.Margin.Left + 27 - size / 2); Canvas.SetTop(mote, tap.Margin.Top + 22 - size / 2); host.Children.Add(mote);
            }
        _faucetFeedbackClock.Restart(); UpdateFaucetFeedback(0);
        if (_faucetFeedbackTimer == null)
        {
            _faucetFeedbackTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
            _faucetFeedbackTimer.Tick += (_, _) => UpdateFaucetFeedback(_faucetFeedbackClock.Elapsed.TotalMilliseconds);
        }
        _faucetFeedbackTimer.Start();
    }
    private void UpdateFaucetFeedback(double ms)
    {
        if (!_vatOnScreen || !AmbientFxCanvas.Env.AllowTransitions) { StopFaucetFeedback(); return; }
        if (_faucetFeedbackPour)
        {
            double t = Math.Clamp(ms / 340, 0, 1), u = Math.Min(1, t / .56);
            double scale = t < .56 ? 2.1 + (.86 - 2.1) * (1 - Math.Pow(1 - u, 3)) : .86 + .14 * (t < .78 ? 4 * Math.Pow((t - .56) / .44, 3) : 1 - Math.Pow(-2 * (t - .56) / .44 + 2, 3) / 2);
            _faucetStamp.ScaleX = _faucetStamp.ScaleY = scale;
            if (ms >= _faucetNextSettle) { _faucetNextSettle = ms + 250; if (VatGlass?.IsPouring != true) _faucetSettleAt = Math.Min(_faucetSettleAt, ms); }
            double lean = double.IsPositiveInfinity(_faucetSettleAt) ? 14 * (1 - Math.Pow(1 - Math.Clamp(ms / 240, 0, 1), 2)) : 14 * Math.Pow(1 - Math.Clamp((ms - _faucetSettleAt) / 240, 0, 1), 2);
            _faucetLean.Angle = lean;
            if (FaucetControl<Canvas>("ProfileVatSparks") is { } host)
                for (int i = host.Children.Count - 1; i >= 0; i--)
                {
                    var mote = (Ellipse)host.Children[i]; var (x, y, duration) = ((double, double, double))mote.Tag!;
                    double p = Math.Clamp(ms / duration, 0, 1), ease = 1 - (1 - p) * (1 - p);
                    ((TranslateTransform)mote.RenderTransform!).X = x * ease; ((TranslateTransform)mote.RenderTransform!).Y = y * ease; mote.Opacity = 1 - p;
                    if (p >= 1) host.Children.RemoveAt(i);
                }
            if (ms >= 340 && ms >= _faucetSettleAt + 240 && (FaucetControl<Canvas>("ProfileVatSparks")?.Children.Count ?? 0) == 0) StopFaucetFeedback();
        }
        else
        {
            double[] amps = { 0, -2.6, 2.2, -1.6, 1.1, -.6, .25, 0 };
            double p = Math.Clamp(ms / 420, 0, 1) * 7; int i = Math.Min(6, (int)p);
            _faucetShake.X = amps[i] + (amps[i + 1] - amps[i]) * (p - i);
            double d = Math.Clamp(ms / 520, 0, 1); _faucetDropFall.Y = 10 * d * d;
            if (FaucetControl<Ellipse>("ProfileVatFaucetDrop") is { } drop) drop.Opacity = d < .2 ? .95 * d / .2 : .95 * (1 - d) / .8;
            if (ms >= 520) StopFaucetFeedback();
        }
    }
    private void StopFaucetFeedback()
    {
        _faucetFeedbackTimer?.Stop(); _faucetFeedbackClock.Stop(); _faucetStamp.ScaleX = _faucetStamp.ScaleY = 1; _faucetLean.Angle = _faucetShake.X = _faucetDropFall.Y = 0;
        if (FaucetControl<Ellipse>("ProfileVatFaucetDrop") is { } drop) drop.Opacity = 0;
        FaucetControl<Canvas>("ProfileVatSparks")?.Children.Clear();
    }
    // WPF MainWindow.ProfileFaucet.cs:1132-1142: the routing row and its slider stay in charge.
    private static void FireFaucetPourHaptic()
    {
        try
        {
            var haptics = CoreHaptics.Service; var rule = haptics?.Settings.V2.Rule(HapticEventKind.LevelUp);
            if (rule == null || !rule.Enabled || rule.Intensity <= 0) return;
            _ = haptics!.PostEvent(HapticEventKind.LevelUp, Math.Clamp(rule.Intensity * .35, 0, 1));
        }
        catch (Exception ex) { Serilog.Log.Debug("Faucet haptic: {E}", ex.Message); }
    }
}
