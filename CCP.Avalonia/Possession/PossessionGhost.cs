using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace ConditioningControlPanel.Avalonia.Possession;

// WPF Services/Possession/Ghost.cs:140-190, 294-343: snapshot, hide and restore.
internal sealed class PossessionGhost : IDisposable
{
    private static readonly List<PossessionGhost> Live = new();
    private readonly Canvas _layer;
    private readonly Control _target;
    private readonly RenderTargetBitmap _bitmap;
    private IDisposable? _opacity, _hitTest;
    private bool _disposed;
    internal Image Image { get; }

    private PossessionGhost(Control target, Canvas layer, RenderTargetBitmap bitmap, Rect bounds)
    {
        _target = target; _layer = layer; _bitmap = bitmap;
        Image = new Image { Source = bitmap, Width = bounds.Width, Height = bounds.Height,
            Stretch = Stretch.Fill, IsHitTestVisible = false };
        Canvas.SetLeft(Image, bounds.X); Canvas.SetTop(Image, bounds.Y);
        layer.Children.Add(Image);
        Live.Add(this);
    }

    internal static PossessionGhost? Capture(Control target, Canvas layer)
    {
        if (!target.IsEffectivelyVisible || target.Bounds.Width <= 0 || target.Bounds.Height <= 0 ||
            target.TransformToVisual(layer) is not { } transform) return null;
        var bounds = new Rect(target.Bounds.Size).TransformToAABB(transform);
        var scale = TopLevel.GetTopLevel(target)?.RenderScaling ?? 1;
        var pixels = PixelSize.FromSize(target.Bounds.Size, scale);
        if (pixels.Width <= 0 || pixels.Height <= 0 || pixels.Width > 8192 || pixels.Height > 8192) return null;
        var bitmap = new RenderTargetBitmap(pixels, new Vector(96 * scale, 96 * scale));
        try { bitmap.Render(target); return new PossessionGhost(target, layer, bitmap, bounds); }
        catch { bitmap.Dispose(); return null; }
    }

    internal void Hide(bool disableHitTesting = false)
    {
        if (_disposed || _opacity != null) return;
        // Animation priority leaves local values, styles and bindings intact underneath the lease.
        _opacity = _target.SetValue(global::Avalonia.Visual.OpacityProperty, 0d, BindingPriority.Animation);
        if (disableHitTesting) _hitTest = _target.SetValue(InputElement.IsHitTestVisibleProperty, false, BindingPriority.Animation);
    }

    internal void Restore() => Dispose();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _hitTest?.Dispose();
        _opacity?.Dispose();
        _layer.Children.Remove(Image);
        _bitmap.Dispose();
        Live.Remove(this);
    }

    internal static void UndoAll()
    {
        foreach (var ghost in Live.ToArray()) ghost.Dispose();
    }
}
