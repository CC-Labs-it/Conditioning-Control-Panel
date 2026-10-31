using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

// WPF EmiDeskWindow.DeskItems: pose-driven layers, with no independent animation.
public partial class EmiDeskWindow
{
    private DeskItemLook _deskLook = DeskItemLook.Empty;
    private readonly Dictionary<string, Bitmap> _deskCache = new(StringComparer.Ordinal);
    private readonly HashSet<string> _deskBroken = new(StringComparer.Ordinal);
    public DeskItemLook DeskItems => _deskLook;

    private void HookDeskItems()
    {
        PrizeOwnership.GrantsChanged += OnDeskGrantsChanged;
        RefreshDeskItems();
    }

    private void UnhookDeskItems()
    {
        PrizeOwnership.GrantsChanged -= OnDeskGrantsChanged;
        StandDown(this.FindControl<Image>("DeskHatOverImage")!);
        StandDown(this.FindControl<Image>("DeskToyOverImage")!);
        ClearDeskCache();
    }

    private void OnDeskGrantsChanged()
    {
        if (Dispatcher.UIThread.CheckAccess()) RefreshDeskItems();
        else Dispatcher.UIThread.Post(RefreshDeskItems);
    }

    public void RefreshDeskItems()
    {
        if (_closingForGood) return;
        var s = CoreSettings.Current;
        SetDeskItems(EmiDeskItemRules.Clamp(new DeskItemLook(s.EmiDeskHat, s.EmiDeskToy),
            PrizeOwnership.KnownGrants()));
    }

    // As on WPF, callers pass an already clamped look. No settings or server writes here.
    public void SetDeskItems(DeskItemLook look)
    {
        if (_closingForGood || look == _deskLook) return;
        _deskLook = look;
        StandDown(this.FindControl<Image>("DeskHatOverImage")!);
        StandDown(this.FindControl<Image>("DeskToyOverImage")!);
        ClearDeskCache();
        PaintDeskItems();
    }

    private void ClearDeskCache()
    {
        foreach (var bitmap in _deskCache.Values) bitmap.Dispose();
        _deskCache.Clear();
    }

    private void PaintDeskItems()
    {
        PaintDeskLayer(this.FindControl<Image>("DeskHatOverImage")!, _deskLook.Hat);
        PaintDeskLayer(this.FindControl<Image>("DeskToyOverImage")!, _deskLook.Toy);
    }

    private void PaintDeskLayer(Image layer, string? id)
    {
        try
        {
            if (id == null || _deskBroken.Contains(id)) { StandDown(layer); return; }
            var key = id + "/" + _pose;
            if (!_deskCache.TryGetValue(key, out var bitmap))
            {
                var path = EmiDeskItemRules.OverPath(id, _pose);
                if (path == null)
                {
                    _deskBroken.Add(id);
                    StandDown(layer);
                    return;
                }
                bitmap = new Bitmap(path);
                _deskCache[key] = bitmap;
            }
            layer.Source = bitmap;
            layer.IsVisible = true;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] desk item paint failed for {Item}", id);
            StandDown(layer);
        }
    }

    private static void StandDown(Image layer)
    {
        layer.IsVisible = false;
        layer.Source = null;
    }
}
