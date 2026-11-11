using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;

// WPF LeashSurfaces.OnSnapshot/TryShowWaitingAsk (:104-124): a newly arrived offer pops up as the one ask
// (LeashAskWindow), held until the panel or launcher is up and until no ask or snap is on screen.
internal sealed class LeashOfferSurfaces : IDisposable
{
    private readonly Func<Window?> _owner;
    private readonly HashSet<string> _seen = new();
    private readonly List<LeashOffer> _waiting = new();
    private ILeashService? _service;
    private string? _account;
    internal LeashOfferSurfaces(Func<Window?> owner)
    {
        _owner = owner;
        LeashAskWindow.Freed += TryShow;
    }

    internal void Rebind()
    {
        var next = LeashHead.Service;
        var account = CoreSettings.Current.UnifiedId;
        var serviceChanged = !ReferenceEquals(next, _service);
        if (serviceChanged || account != _account)
        {
            if (_service != null) _service.SnapshotChanged -= OnSnapshot;
            _service = next; _account = account; _seen.Clear(); _waiting.Clear();
            if (_service != null)
            {
                _service.SnapshotChanged += OnSnapshot;
                if (serviceChanged && _service.Available) Process(_service.Snapshot);
            }
        }
        TryShow();
    }

    private static string OfferKey(LeashOffer o) => o.From.Id + "@" + o.At.ToUnixTimeSeconds();
    private void OnSnapshot(LeashSnapshot snapshot)
    {
        var source = _service;
        void Apply()
        {
            if (!ReferenceEquals(source, _service)) return;
            if (!ReferenceEquals(source, LeashHead.Service) || _account != CoreSettings.Current.UnifiedId) { Rebind(); return; }
            if (source?.Available == true) Process(snapshot);
        }
        if (Dispatcher.UIThread.CheckAccess()) Apply(); else Dispatcher.UIThread.Post(Apply);
    }
    private void Process(LeashSnapshot snapshot)
    {
        foreach (var offer in snapshot.Offers)
        {
            if (!_seen.Add(OfferKey(offer))) continue;
            _waiting.RemoveAll(o => o.From.Id == offer.From.Id);
            _waiting.Add(offer);
        }
        _waiting.RemoveAll(o => !snapshot.Offers.Any(current => current.From.Id == o.From.Id));
        TryShow();
    }
    private void TryShow()
    {
        if (_service?.Available != true || _waiting.Count == 0 || LeashAskWindow.Current != null || LeashAskWindow.Snap != null) return;
        var owner = _owner();
        if (owner == null || !owner.IsVisible || owner.WindowState == WindowState.Minimized) return;
        var offer = _waiting[0];
        _waiting.RemoveAt(0);
        // WPF ShowAsk reads the live service (LeashSurfaces.cs:211,217).
        if (LeashAskWindow.Open(offer, () => LeashHead.Service, owner) == null) _waiting.Insert(0, offer);
    }
    public void Dispose()
    {
        LeashAskWindow.Freed -= TryShow;
        if (_service != null) _service.SnapshotChanged -= OnSnapshot;
        _service = null; _waiting.Clear(); _seen.Clear();
    }
}
