using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class LauncherWindow
{
    // A sibling shadow border keeps the WPF tile glow without an Effect below a looping adorner.
    private void AttachTileGlow(TileFx fx)
    {
        if (fx.Glow != null || !AmbientFxCanvas.Env.AllowGlow(AmbientFxCanvas.Env.CurrentTier)) return;
        var layer = AdornerLayer.GetAdornerLayer(fx.Tile);
        if (layer == null) return;
        fx.Glow = new Border
        {
            IsHitTestVisible = false, Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(TileRadius), Opacity = fx.GlowOpacity * fx.Tile.Opacity * RootGrid.Opacity,
            BoxShadow = new BoxShadows(new BoxShadow
            {
                Color = fx.Hue, Blur = Math.Min(20, AmbientFxCanvas.Env.MaxGlowBlurRadius(AmbientFxCanvas.Env.CurrentTier)),
            }),
            IsVisible = TileGlowShown(fx),
        };
        AdornerLayer.SetAdornedElement(fx.Glow, fx.Tile);
        layer.Children.Add(fx.Glow);
        fx.GlowWatch = EffectiveVisibility.Watch(fx.Tile, () =>
        {
            if (fx.Glow != null) fx.Glow.IsVisible = TileGlowShown(fx);
        });
    }

    // The window adorner layer draws above LockdownVeil; WPF's Effect glow sat under it, so hide while veiled.
    private bool TileGlowShown(TileFx fx) => fx.Tile.IsEffectivelyVisible && !LockdownVeil.IsVisible;

    private void RefreshTileGlowVisibility()
    {
        foreach (var fx in _tileFx) if (fx.Glow != null) fx.Glow.IsVisible = TileGlowShown(fx);
    }

    private static void RemoveTileGlow(TileFx fx)
    {
        fx.GlowWatch?.Dispose(); fx.GlowWatch = null;
        if (fx.Glow?.Parent is Panel p) p.Children.Remove(fx.Glow);
        fx.Glow = null;
    }

    private void ClearTileGlows()
    {
        foreach (var fx in _tileFx) RemoveTileGlow(fx);
    }

    internal Border? TileGlow(Border tile) => _tileFx.Find(fx => ReferenceEquals(fx.Tile, tile))?.Glow;
}
