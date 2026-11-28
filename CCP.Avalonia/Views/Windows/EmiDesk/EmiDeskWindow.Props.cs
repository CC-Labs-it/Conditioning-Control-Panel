using System;
using System.Threading;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

public partial class EmiDeskWindow
{
    private string? _propKey;
    private Bitmap? _propBitmap;
    private DispatcherTimer? _propTimer;
    private CancellationTokenSource? _propAnimation;
    public bool PropUp => _propKey != null;

    // WPF EmiDeskWindow.Props.cs:56-108, anchored to the current body box.
    private void LayoutProp()
    {
        var prop = EmiProps.Get(_propKey);
        if (prop == null || _propBitmap == null) return;
        double bw = _bodyWidth, bh = bw * BodyAspect;
        double aspect = (double)_propBitmap.PixelSize.Width / _propBitmap.PixelSize.Height;
        double w = prop.Sizing == EmiProps.Fit.Height ? bh * prop.Frac * aspect : bw * prop.Frac;
        double h = prop.Sizing == EmiProps.Fit.Height ? bh * prop.Frac : w / aspect;
        _propImage.Width = Math.Max(1, w);
        _propImage.Height = Math.Max(1, h);
        _propImage.Margin = new Thickness(0, 0, bw * (1 - EmiProps.RightFrac), bh * (1 - EmiProps.BottomFrac));
        ((RotateTransform)((TransformGroup)_propImage.RenderTransform!).Children[0]).Angle = prop.TiltDeg;
    }

    // WPF Props.cs:118: unavailable art declines without disturbing an existing plate.
    public void ShowProp(string? key, int holdMs = 0)
    {
        if (_closingForGood) return;
        var prop = EmiProps.Get(key);
        if (prop == null) return;
        try
        {
            var path = EmiProps.Path(prop.Key);
            if (path == null) return;
            var image = new Bitmap(path);
            _propTimer?.Stop();
            CancelPropAnimation();
            var old = _propBitmap;
            _propBitmap = image;
            _propKey = prop.Key;
            _propImage.Source = image;
            old?.Dispose();
            LayoutProp();
            _propImage.IsVisible = true;
            RunPropRise(true);
            _propTimer ??= new DispatcherTimer();
            _propTimer.Stop();
            _propTimer.Interval = TimeSpan.FromMilliseconds(holdMs > 0 ? holdMs : EmiProps.HoldMs);
            _propTimer.Tick -= OnPropHoldOver;
            _propTimer.Tick += OnPropHoldOver;
            _propTimer.Start();
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ShowProp failed for {Key}", key); }
    }

    private void OnPropHoldOver(object? sender, EventArgs e) => HideProp();

    // WPF Props.cs:184: interruption is idempotent, teardown drops without a slide.
    public void HideProp(bool animate = true)
    {
        _propTimer?.Stop();
        CancelPropAnimation();
        bool wasUp = _propKey != null;
        _propKey = null;
        if (wasUp && animate && AmbientFxCanvas.Env.Level == MotionLevel.Full)
            RunPropRise(false);
        else ClearProp();
    }

    private void CancelPropAnimation()
    {
        _propAnimation?.Cancel();
        _propAnimation?.Dispose();
        _propAnimation = null;
    }

    private void ClearProp()
    {
        ((TranslateTransform)((TransformGroup)_propImage.RenderTransform!).Children[1]).Y = 0;
        _propImage.IsVisible = false;
        _propImage.Source = null;
        _propBitmap?.Dispose();
        _propBitmap = null;
    }

    private void RunPropRise(bool up)
    {
        var rise = (TranslateTransform)((TransformGroup)_propImage.RenderTransform!).Children[1];
        if (AmbientFxCanvas.Env.Level != MotionLevel.Full)
        {
            rise.Y = 0;
            if (!up) ClearProp();
            return;
        }
        double travel = _propImage.Height * EmiProps.RiseFrac;
        var cts = new CancellationTokenSource();
        _propAnimation = cts;
        Tween(EmiProps.RiseMs, cts.Token, p =>
        {
            rise.Y = travel * (up ? 1 - Math.Sin(p * Math.PI / 2) : 1 - Math.Cos(p * Math.PI / 2));
            if (p >= 1 && !up) ClearProp();
        });
    }
}
