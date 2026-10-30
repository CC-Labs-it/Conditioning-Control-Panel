using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Services.EmiDesk;
using ConditioningControlPanel.Services.Prizes;
using Serilog;

namespace ConditioningControlPanel;

/// <summary>
/// THE PRIZE COUNTER DESK ITEMS: the hat and the held toy the account owns and picked (EmiDeskItems), painted
/// on <c>DeskHatOverImage</c> / <c>DeskToyOverImage</c>, which the XAML authors after the outfit overlay so
/// both sit above her face and her garment (THE SKIN LAW, ConditioningControlPanel/CLAUDE.md item 15).
///
/// <para>Same shape as the outfit overlay: repainted from <see cref="SetPose"/> (the one place the body PNG
/// changes), one decoded frame per pose cached per item, and a sheet missing any pose stands the whole layer
/// down for the sitting. No animation of its own, so nothing here asks Gentle visuals for anything.</para>
/// </summary>
public partial class EmiDeskWindow
{
    private DeskLook _deskLook = DeskLook.Empty;
    private readonly Dictionary<string, ImageSource> _deskCache = new(StringComparer.Ordinal);
    private readonly HashSet<string> _deskBroken = new(StringComparer.Ordinal);

    /// <summary>What she has on from the Prize Counter right now (tests and the options panel read it).</summary>
    public DeskLook DeskItems => _deskLook;

    private void HookDeskItems()
    {
        PrizeGrants.GrantsChanged += OnDeskGrantsChanged;
        DeskEquipSync.Adopted += OnDeskGrantsChanged;
    }

    private void UnhookDeskItems()
    {
        PrizeGrants.GrantsChanged -= OnDeskGrantsChanged;
        DeskEquipSync.Adopted -= OnDeskGrantsChanged;
    }

    private void OnDeskGrantsChanged()
    {
        try
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(RefreshDeskItems)); return; }
            RefreshDeskItems();
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] desk items grant refresh failed"); }
    }

    /// <summary>
    /// Read the stored pick (clamped to owned grants) and paint it. Called with every outfit refresh (each
    /// summon), when grants change and when a server copy is adopted. Also starts the once-a-launch pull.
    /// </summary>
    public void RefreshDeskItems()
    {
        try
        {
            SetDeskItems(EmiDeskItems.Current());
            _ = DeskEquipSync.PullOnceAsync(a => Dispatcher.BeginInvoke(a));
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] desk items refresh failed"); }
    }

    /// <summary>Put a look on her now (already clamped by the caller). Null ids take that item off.</summary>
    public void SetDeskItems(DeskLook look)
    {
        if (look == _deskLook) return;
        Log.Information("[EmiDesk] desk items -> hat {Hat}, toy {Toy}", look.Hat ?? "none", look.Toy ?? "none");
        _deskLook = look;
        _deskCache.Clear();
        PaintDeskItems();
    }

    private void PaintDeskItems()
    {
        PaintDeskLayer(DeskHatOverImage, _deskLook.Hat);
        PaintDeskLayer(DeskToyOverImage, _deskLook.Toy);
    }

    private void PaintDeskLayer(Image layer, string? itemId)
    {
        try
        {
            if (itemId == null || _deskBroken.Contains(itemId))
            {
                StandDown(layer);
                return;
            }
            var key = itemId + "/" + _pose;
            if (!_deskCache.TryGetValue(key, out var img))
            {
                var path = EmiDeskItems.OverPath(itemId, _pose);
                if (path == null)
                {
                    // Half a sheet is no sheet, as with the outfit overlay.
                    Log.Debug("[EmiDesk] desk item {Item} has no {Pose} frame; layer off", itemId, _pose);
                    _deskBroken.Add(itemId);
                    StandDown(layer);
                    return;
                }
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                img = bmp;
                _deskCache[key] = img;
            }
            if (!ReferenceEquals(layer.Source, img)) layer.Source = img;
            layer.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] desk item paint failed for {Item}", itemId);
            StandDown(layer);
        }
    }

    private static void StandDown(Image layer)
    {
        if (layer.Visibility == Visibility.Collapsed && layer.Source == null) return;
        layer.Visibility = Visibility.Collapsed;
        layer.Source = null;
    }
}
