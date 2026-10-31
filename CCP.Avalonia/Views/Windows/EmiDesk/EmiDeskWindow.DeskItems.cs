using System;
using System.Collections.Generic;
using System.Net.Http;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.EmiDesk;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

// WPF EmiDeskWindow.DeskItems.cs: overlays above outfit and face, one cached frame per pose.
public partial class EmiDeskWindow
{
    private static readonly HttpClient EquipHttp = new() { Timeout = TimeSpan.FromSeconds(10) };
    private DeskEquipmentSync? _equip;
    private readonly Dictionary<string, Bitmap> _deskFrames = new();
    private readonly HashSet<string> _deskBroken = new();
    public DeskEquipmentLook DeskItems { get; private set; } = DeskEquipmentLook.Empty;
    internal static IEnumerable<string>? DeskGrants() => PrizeOwnership.KnownDeskGrants();
    internal static DeskEquipmentLook CurrentDeskItems() => DeskEquipment.Clamp(
        new(CoreSettings.Current.EmiDeskHat, CoreSettings.Current.EmiDeskToy), DeskGrants());
    private void HookDeskItems()
    {
        var url = FriendsHead.BaseUrl(Environment.GetEnvironmentVariable("CCP_USERDATA_DIR"),
            Environment.GetEnvironmentVariable(V2PurchaseHead.EnvVar));
        if (url != null)
        {
            _equip = new(EquipHttp, () => CoreSettings.Current, () => CoreSettings.Save(), FriendsHead.Identity,
                DeskGrants, a => Dispatcher.UIThread.Post(a), url);
            _equip.Adopted += RefreshDeskItems;
        }
        PrizeOwnership.DeskGrantsChanged += OnDeskGrantsChanged;
    }
    private void OnDeskGrantsChanged() => Dispatcher.UIThread.Post(() => { if (!_closingForGood) RefreshDeskItems(); });
    public void RefreshDeskItems()
    {
        DeskItems = CurrentDeskItems();
        PaintDeskItems();
        if (_equip != null) _ = _equip.PullOnceAsync();
    }
    internal void StartDeskEquipment() { _equip?.Resume(); RefreshDeskItems(); }
    internal void StopDeskEquipment() => _equip?.Stop();
    internal void PickDeskItem(string slot, string? id)
    {
        var now = CurrentDeskItems();
        var next = slot == DeskEquipment.HatSlot ? now with { Hat = id } : now with { Toy = id };
        if (_equip != null) _ = _equip.SaveAsync(next);
        else
        {
            var clean = DeskEquipment.Clamp(next, DeskGrants());
            var s = CoreSettings.Current;
            s.EmiDeskHat = clean.Hat; s.EmiDeskToy = clean.Toy;
            s.EmiDeskEquipUpdatedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            s.EmiDeskEquipPushOwed = true; CoreSettings.Save();
        }
        RefreshDeskItems();
    }
    private void PaintDeskItems()
    {
        PaintDeskLayer(this.FindControl<Image>("DeskHatOverImage")!, DeskItems.Hat);
        PaintDeskLayer(this.FindControl<Image>("DeskToyOverImage")!, DeskItems.Toy);
    }
    private void PaintDeskLayer(Image layer, string? id)
    {
        layer.Source = null; layer.IsVisible = false;
        if (id == null || _deskBroken.Contains(id)) return;
        try
        {
            var key = id + "/" + _pose;
            if (!_deskFrames.TryGetValue(key, out var bitmap))
            {
                var path = DeskEquipment.OverPath(id, _pose);
                if (path == null) { _deskBroken.Add(id); return; }
                _deskFrames[key] = bitmap = new Bitmap(path);
            }
            layer.Source = bitmap; layer.IsVisible = true;
        }
        catch { _deskBroken.Add(id); }
    }
    private void UnhookDeskItems()
    {
        PrizeOwnership.DeskGrantsChanged -= OnDeskGrantsChanged;
        _equip?.Dispose();
        foreach (var bitmap in _deskFrames.Values) bitmap.Dispose();
        _deskFrames.Clear();
    }
}
