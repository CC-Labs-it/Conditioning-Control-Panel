using System;
using System.Threading;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

public partial class EmiDeskWindow
{
    private CancellationTokenSource? _bodyMoveScaleAnimation;
    private CancellationTokenSource? _bodyMoveXAnimation;
    private CancellationTokenSource? _bodyMoveYAnimation;
    // Per-window clock seam, so tests can step the same tween without wall-clock delays.
    internal Action<double, CancellationToken, Action<double>>? BodyMoveTween;

    // WPF EmiDeskWindow.xaml.cs:1097-1167. Scale has its own slot so dismissal's CRT
    // animation and the click squash can run at the same time.
    partial void OnBodyMoveCore(string move, ref bool handled)
    {
        double ms, x = 0, y = 0;
        int repeats = 0;
        bool scale = false;
        switch (move)
        {
            case "bounce": ms = 340; x = 1.08; y = 0.94; scale = true; break;
            case "thud": ms = 280; x = 1.10; y = 0.88; scale = true; break;
            case "nod": ms = 1800; y = 4; repeats = 2; break;
            case "droop": ms = 600; y = 6; repeats = 1; break;
            case "shiver": ms = 2560; x = 2; repeats = 4; break;
            default: return;
        }
        handled = true;
        bool horizontal = !scale && x != 0;
        StopBodyMove(scale, horizontal);
        var cts = new CancellationTokenSource();
        ref var animation = ref BodyMoveAnimation(scale, horizontal);
        animation = cts;
        void Apply(double p)
        {
            if (cts.IsCancellationRequested) return;
            if (p >= 1) { StopBodyMove(scale, horizontal); return; }
            if (scale)
            {
                double u = p < 0.4 ? p / 0.4 : (p - 0.4) / 0.6;
                double eased = 1 - Math.Pow(1 - u, 3);
                _bodyMoveScale.ScaleX = p < 0.4 ? 1 + (x - 1) * eased : x + (1 - x) * eased;
                _bodyMoveScale.ScaleY = p < 0.4 ? 1 + (y - 1) * eased : y + (1 - y) * eased;
            }
            else
            {
                double phase = (p * repeats) % 1;
                double triangle = phase < 0.5 ? phase * 2 : (1 - phase) * 2;
                if (x != 0) _bodyMoveShift.X = -x + 2 * x * triangle;
                else _bodyMoveShift.Y = y * triangle;
            }
        }
        if (BodyMoveTween is { } clock) clock(ms, cts.Token, Apply);
        else Tween(ms, cts.Token, Apply);
    }

    // WPF BeginAnimation replaces only the targeted property, leaving the other axes live.
    private ref CancellationTokenSource? BodyMoveAnimation(bool scale, bool horizontal)
    {
        if (scale) return ref _bodyMoveScaleAnimation;
        if (horizontal) return ref _bodyMoveXAnimation;
        return ref _bodyMoveYAnimation;
    }

    private void StopBodyMove(bool scale, bool horizontal)
    {
        ref var animation = ref BodyMoveAnimation(scale, horizontal);
        animation?.Cancel();
        animation?.Dispose();
        animation = null;
        if (scale) _bodyMoveScale.ScaleX = _bodyMoveScale.ScaleY = 1;
        else if (horizontal) _bodyMoveShift.X = 0;
        else _bodyMoveShift.Y = 0;
    }

    private void StopBodyMove()
    {
        StopBodyMove(true, false);
        StopBodyMove(false, true);
        StopBodyMove(false, false);
    }
}
