using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube;

public partial class AvatarTubeWindow
{
    private PixelPoint? _eggHome;
    private bool _eggWasAttached;

    internal bool CanPerformBubbleEgg => CanPerformPossessionMove && _possessionHome == null
        && _eggHome == null && IsAvatarVisibleOnScreen;

    internal async Task GlideToBubbleAsync(AmbientBubble bubble, CancellationToken ct)
    {
        if (!CanPerformBubbleEgg) throw new OperationCanceledException();
        var center = BubbleOverlay.CenterPx(bubble);
        var point = new PixelPoint((int)center.X, (int)center.Y);
        var screen = Screens.ScreenFromPoint(point) ?? Screens.Primary;
        if (screen == null) throw new OperationCanceledException();
        var radius = bubble.Size * BubbleOverlay.ScaleFor(bubble) / 2;
        var work = screen.WorkingArea;
        var size = TubePixelSize;
        var x = center.X > work.X + work.Width / 2.0
            ? center.X - radius - 24 - size.Width : center.X + radius + 24;
        var target = ClampPossessionPoint(new PixelPoint((int)x, (int)(center.Y - size.Height / 2.0)), size, work);
        _eggHome = Position;
        _eggWasAttached = _isAttached;
        if (_eggWasAttached)
        {
            _restoringPlacement = true;
            try { SetAttached(false); }
            finally { _restoringPlacement = false; }
        }
        InputRect = null;
        RequestInputShape();
        await GlidePossessionStepsAsync(target, ct);
    }

    internal async Task ReturnFromBubbleAsync()
    {
        if (_eggHome is not { } home) return;
        try
        {
            if (!_eggWasAttached || _parentWindow == null) await GlidePossessionStepsAsync(home, CancellationToken.None);
        }
        finally
        {
            _eggHome = null;
            if (_eggWasAttached)
            {
                _restoringPlacement = true;
                try { SetAttached(true); UpdatePosition(); }
                finally { _restoringPlacement = false; }
            }
            _eggWasAttached = false;
            RequestInputShape();
        }
    }
}
