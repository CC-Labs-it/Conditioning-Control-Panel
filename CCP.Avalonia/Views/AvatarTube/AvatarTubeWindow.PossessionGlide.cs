// WPF AvatarTube/AvatarTubeWindow.Windowing.cs:1771-1899: the warden's physical-pixel movement.
using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube;

public partial class AvatarTubeWindow
{
    private PixelPoint? _possessionHome;
    private bool _possessionWasAttached;

    internal bool? PossessionMoveSupportedOverride { get; set; }

    // Native Wayland does not allow clients to position their windows. Unknown backends are unsafe too.
    public bool CanPerformPossessionMove => IsVisible && CoreSettings.Current.AvatarEnabled
        && !IsCompanionBusy(0)
        && (PossessionMoveSupportedOverride ?? (X11Overlay.BackendOf(TryGetPlatformHandle()) is OverlayBackend.X11 or OverlayBackend.Win32));

    internal static PixelPoint ClampPossessionPoint(PixelPoint point, PixelSize size, PixelRect work) => new(
        Math.Clamp(point.X, work.X, Math.Max(work.X, work.Right - size.Width)),
        Math.Clamp(point.Y, work.Y, Math.Max(work.Y, work.Bottom - size.Height)));

    internal static PixelPoint PossessionRectPoint(PixelRect target, PixelSize size, PixelRect work)
    {
        var cx = target.X + target.Width / 2;
        var cy = target.Y + target.Height / 2;
        return ClampPossessionPoint(new PixelPoint(cx > work.X + work.Width / 2
            ? target.X - 20 - size.Width : target.Right + 20, cy - size.Height / 2), size, work);
    }

    public async Task GlideToScreenRectAsync(PixelRect rect, CancellationToken ct)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            await Dispatcher.UIThread.InvokeAsync(() => GlideToScreenRectAsync(rect, ct));
            return;
        }
        var centre = new PixelPoint(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        var screen = Screens.ScreenFromPoint(centre) ?? Screens.Primary;
        if (screen != null) await GlidePossessionAsync(
            PossessionRectPoint(rect, TubePixelSize, screen.WorkingArea), ct);
    }

    public async Task GlideToScreenPointAsync(PixelPoint point, CancellationToken ct, bool clampToWorkArea = true)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            await Dispatcher.UIThread.InvokeAsync(() => GlideToScreenPointAsync(point, ct, clampToWorkArea));
            return;
        }
        if (clampToWorkArea)
        {
            var screen = Screens.ScreenFromPoint(point) ?? Screens.Primary;
            if (screen == null) return;
            point = ClampPossessionPoint(point, TubePixelSize, screen.WorkingArea);
        }
        await GlidePossessionAsync(point, ct);
    }

    private async Task GlidePossessionAsync(PixelPoint target, CancellationToken ct)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            await Dispatcher.UIThread.InvokeAsync(() => GlidePossessionAsync(target, ct));
            return;
        }
        if (!CanPerformPossessionMove || ct.IsCancellationRequested) return;
        if (_possessionHome == null)
        {
            _possessionHome = Position;
            _possessionWasAttached = _isAttached;
            InputRect = null;
            RequestInputShape();
        }
        await GlidePossessionStepsAsync(target, ct);
    }

    private async Task GlidePossessionStepsAsync(PixelPoint target, CancellationToken ct)
    {
        var start = Position;
        for (int i = 1; i <= 14; i++)
        {
            ct.ThrowIfCancellationRequested();
            double t = i / 14.0;
            t = t < .5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;
            Position = new PixelPoint((int)Math.Round(start.X + (target.X - start.X) * t),
                (int)Math.Round(start.Y + (target.Y - start.Y) * t));
            await Task.Delay(28, ct);
        }
    }

    public async Task ReturnHomeAsync(CancellationToken ct)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            await Dispatcher.UIThread.InvokeAsync(() => ReturnHomeAsync(ct));
            return;
        }
        if (_possessionHome is not { } home) return;
        try
        {
            // As in WPF, a detached tube's homeward leg must finish even after cancellation.
            if (!_possessionWasAttached || _parentWindow == null) await GlidePossessionStepsAsync(home, CancellationToken.None);
        }
        finally
        {
            _possessionHome = null;
            if (_possessionWasAttached && _parentWindow != null) UpdatePosition();
            _possessionWasAttached = false;
        }
    }
}
