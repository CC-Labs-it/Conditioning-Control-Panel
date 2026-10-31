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
    internal Dictionary<string, LeashHolderCard> HolderCards { get; } = new();

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
        if (_service != null) { _service.SnapshotChanged += OnSnapshot; _service.ReceiptsChanged += OnReceipts; }
        Render();
    }

    private void Unsubscribe()
    {
        if (_service != null) { _service.SnapshotChanged -= OnSnapshot; _service.ReceiptsChanged -= OnReceipts; }
    }
    private void OnReceipts() { foreach (var card in HolderCards.Values.ToArray()) card.Render(); }
    private void OnSnapshot(LeashSnapshot _) => Render();

    private void Render()
    {
        var snapshot = LeashSnapshot.Empty;
        try { if (_service?.Available == true) snapshot = _service.Snapshot; } catch { }
        var me = snapshot.Me;
        SectionHost.Children.Clear();
        if (me != null)
        {
            if (_self == null) _self = new LeashSelfCard(me, () => _service);
            else _self.Update(me);
            SectionHost.Children.Add(_self);
            // WPF NoteOnScreen: assignments are seen here; punishments are seen on the gate.
            if (IsEffectivelyVisible && me.Assignment is { } a) _service?.NoteShown(a.Aid);
        }
        else _self = null;
        var keep = new HashSet<string>();
        foreach (var held in snapshot.Holding)
        {
            keep.Add(held.Who.Id);
            if (!HolderCards.TryGetValue(held.Who.Id, out var card))
                HolderCards[held.Who.Id] = card = new LeashHolderCard(held, () => _service);
            else card.Update(held);
            SectionHost.Children.Add(card);
        }
        foreach (var gone in HolderCards.Keys.Where(id => !keep.Contains(id)).ToArray()) HolderCards.Remove(gone);
        EmptyPanel.IsVisible = me == null && HolderCards.Count == 0;
    }
}
