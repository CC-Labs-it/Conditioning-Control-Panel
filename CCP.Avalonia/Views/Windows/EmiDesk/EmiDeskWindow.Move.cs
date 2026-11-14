using System;
using System.Threading;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

public partial class EmiDeskWindow
{
    private CancellationTokenSource? _moveXAnim, _moveYAnim;
    // WPF animations mask this base value while the show continues to update it.
    private double _moveYBase;

    // WPF EmiDeskWindow.xaml.cs:1094-1160. The CRT scale is shared with the power
    // effect; X and Y offsets have independent slots, just as BeginAnimation does.
    private void RunBodyMove(string move)
    {
        bool handled = false;
        try { OnBodyMoveCore(move, ref handled); }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] body-move seam threw"); }
        if (handled || _closingForGood) return;

        switch (move)
        {
            case "bounce": ScalePulse(340, 1.08, .94); break;
            case "thud": ScalePulse(280, 1.10, .88); break;
            case "nod": MoveOffset(false, 4, 450, 2); break;
            case "droop": MoveOffset(false, 6, 300, 1); break;
            case "shiver": MoveOffset(true, 2, 320, 4); break;
        }
    }

    private void ScalePulse(int ms, double sx, double sy)
    {
        double fromX = _crtScale.ScaleX, fromY = _crtScale.ScaleY;
        CancelCrt();
        var cts = new CancellationTokenSource();
        _crtAnim = cts;
        Tween(ms, cts.Token, p =>
        {
            // CubicEaseOut to the 40% keyframe and back to rest.
            double t = p <= .4 ? p / .4 : (p - .4) / .6;
            double eased = 1 - Math.Pow(1 - t, 3);
            _crtScale.ScaleX = p <= .4 ? fromX + (sx - fromX) * eased : sx + (1 - sx) * eased;
            _crtScale.ScaleY = p <= .4 ? fromY + (sy - fromY) * eased : sy + (1 - sy) * eased;
        });
    }

    private void MoveOffset(bool horizontal, double distance, int halfMs, int repeats)
    {
        var old = horizontal ? _moveXAnim : _moveYAnim;
        old?.Cancel(); old?.Dispose();
        var cts = new CancellationTokenSource();
        if (horizontal) _moveXAnim = cts; else _moveYAnim = cts;
        Tween(halfMs * 2 * repeats, cts.Token, p =>
        {
            double half = p * repeats * 2;
            double phase = half >= repeats * 2 ? 0 : half % 2;
            double value = phase <= 1 ? phase : 2 - phase;
            if (horizontal) _moveShift.X = -distance + 2 * distance * value;
            else _moveShift.Y = distance * value;
            // WPF FillBehavior.Stop reveals the latest base value under the animation.
            if (p >= 1)
            {
                if (horizontal) { _moveXAnim = null; _moveShift.X = 0; }
                else { _moveYAnim = null; _moveShift.Y = _moveYBase; }
                cts.Dispose();
            }
        });
    }

    private void SetMoveYBase(double value)
    {
        _moveYBase = value;
        if (_moveYAnim == null) _moveShift.Y = value;
    }
}
