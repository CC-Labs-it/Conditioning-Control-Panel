using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

/// <summary>Social &gt; Leash. WPF page repaint and the leashed account's own card.</summary>
public partial class LeashTabView : UserControl
{
    private readonly Func<ILeashService?> _resolve;
    private ILeashService? _service;
    private LeashSelfCard? _self;
    private readonly Dictionary<string, LeashHolderCard> _holders = new();

    public LeashTabView() : this(() => LeashHead.Service) { }
    internal LeashTabView(Func<ILeashService?> service)
    {
        _resolve = service;
        InitializeComponent();
        EmptyButton.Click += (_, _) => (TopLevel.GetTopLevel(this) as MainShellWindow)?.ShowTab("friends");
        DetachedFromVisualTree += (_, _) => Unsubscribe();
        PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty && !IsVisible) Unsubscribe(); };
    }

    internal bool ShowingEmpty => EmptyPanel.IsVisible;
    internal LeashSelfCard? SelfCard => _self;

    internal void OnShown()
    {
        Unsubscribe();
        try { _service = _resolve(); } catch { _service = null; }
        if (_service != null) { _service.SnapshotChanged += OnSnapshot; _service.ReceiptsChanged += Render; }
        Render();
    }

    private void Unsubscribe()
    {
        if (_service != null) { _service.SnapshotChanged -= OnSnapshot; _service.ReceiptsChanged -= Render; }
        foreach (var card in _holders.Values) card.Fold();
    }
    private void OnSnapshot(LeashSnapshot _) => Render();

    private void Render()
    {
        MyLeash? me = null;
        try { if (_service?.Available == true) me = _service.Snapshot.Me; } catch { }
        // WPF LeashDrawerSection.cs:113-150: keep each held card and its open sheet across polls.
        var holding = _service?.Available == true ? _service.Snapshot.Holding : Array.Empty<HeldLeash>();
        var cards = new List<Control>();
        if (me != null)
        {
            if (_self == null) _self = new LeashSelfCard(me, () => _service);
            else _self.Update(me);
            cards.Add(_self);
            // WPF NoteOnScreen: assignments are seen here; punishments are seen on the gate.
            if (IsEffectivelyVisible && me.Assignment is { } a) _service?.NoteShown(a.Aid);
        }
        else _self = null;
        foreach (var held in holding)
        {
            if (!_holders.TryGetValue(held.Who.Id, out var card))
                _holders[held.Who.Id] = card = new LeashHolderCard(held, () => _service);
            else card.Update(held);
            cards.Add(card);
        }
        foreach (var id in _holders.Keys.Except(holding.Select(h => h.Who.Id)).ToArray()) _holders.Remove(id);
        foreach (var gone in SectionHost.Children.Where(c => !cards.Contains(c)).ToArray()) SectionHost.Children.Remove(gone);
        for (var i = 0; i < cards.Count; i++)
            if (i >= SectionHost.Children.Count || !ReferenceEquals(SectionHost.Children[i], cards[i]))
            {
                SectionHost.Children.Remove(cards[i]);
                SectionHost.Children.Insert(i, cards[i]);
            }
        EmptyPanel.IsVisible = me == null && holding.Count == 0;
    }
}
