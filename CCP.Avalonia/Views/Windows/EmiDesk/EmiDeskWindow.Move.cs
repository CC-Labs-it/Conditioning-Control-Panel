using System;
using System.Threading;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

public partial class EmiDeskWindow
{
    private CancellationTokenSource? _moveXAnim, _moveYAnim;

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
        CancelCrt();
        var cts = new CancellationTokenSource();
        _crtAnim = cts;
        Tween(ms, cts.Token, p =>
        {
            // CubicEaseOut to the 40% keyframe and back to rest.
            double t = p <= .4 ? p / .4 : (p - .4) / .6;
            double eased = 1 - Math.Pow(1 - t, 3);
            double amount = p <= .4 ? eased : 1 - eased;
            _crtScale.ScaleX = 1 + (sx - 1) * amount;
            _crtScale.ScaleY = 1 + (sy - 1) * amount;
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
            // WPF's offset animations have FillBehavior.Stop.
            if (p >= 1) { if (horizontal) _moveShift.X = 0; else _moveShift.Y = 0; }
        });
    }

    private void CancelBodyMoves()
    {
        _moveXAnim?.Cancel(); _moveXAnim?.Dispose(); _moveXAnim = null;
        _moveYAnim?.Cancel(); _moveYAnim?.Dispose(); _moveYAnim = null;
        _moveShift.X = _moveShift.Y = 0;
    }
}
