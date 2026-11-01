// WPF MainWindow/MainWindow.Leash.cs:80-88 and Controls/Leash/LeashSurfaces.cs:48-76,104-125.
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private ILeashService? _askService;
    private readonly HashSet<string> _seenLeashOffers = new();
    private readonly List<LeashOffer> _waitingLeashAsks = new();

    private void InitializeLeashAsks()
    {
        LeashAskWindow.CurrentClosed += OnLeashAskClosed;
        Closed += (_, _) =>
        {
            LeashAskWindow.CurrentClosed -= OnLeashAskClosed;
            if (_askService != null) _askService.SnapshotChanged -= OnLeashAskSnapshot;
            _askService = null;
            _waitingLeashAsks.Clear();
        };
        RebindLeashAsks();
    }

    // WPF rebinds every two seconds and retries asks when a panel or launcher returns.
    internal void RebindLeashAsks()
    {
        var next = LeashHead.Service;
        if (!ReferenceEquals(next, _askService))
        {
            if (_askService != null) _askService.SnapshotChanged -= OnLeashAskSnapshot;
            _askService = next;
            _seenLeashOffers.Clear();
            if (next != null)
            {
                next.SnapshotChanged += OnLeashAskSnapshot;
                OnLeashAskSnapshot(next.Snapshot);
            }
        }
        TryShowLeashAsk();
    }

    private void OnLeashAskSnapshot(LeashSnapshot snap)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            var service = _askService;
            Dispatcher.UIThread.Post(() => { if (ReferenceEquals(service, _askService)) OnLeashAskSnapshot(snap); });
            return;
        }
        foreach (var offer in snap.Offers)
        {
            if (!_seenLeashOffers.Add(offer.From.Id + "@" + offer.At.ToUnixTimeSeconds())) continue;
            _waitingLeashAsks.RemoveAll(w => w.From.Id == offer.From.Id);
            _waitingLeashAsks.Add(offer);
        }
        _waitingLeashAsks.RemoveAll(w => !snap.Offers.Any(o => o.From.Id == w.From.Id));
        TryShowLeashAsk();
    }

    private void OnLeashAskClosed() => Dispatcher.UIThread.Post(TryShowLeashAsk);

    private void TryShowLeashAsk()
    {
        if (_waitingLeashAsks.Count == 0 || LeashAskWindow.Current != null) return;
        Window? owner = IsVisible && WindowState != WindowState.Minimized ? this : LauncherWindow.Instance;
        if (owner is not { IsVisible: true } || owner.WindowState == WindowState.Minimized) return;
        var offer = _waitingLeashAsks[0];
        _waitingLeashAsks.RemoveAt(0);
        LeashAskWindow.Open(offer, () => _askService, owner);
    }
}
