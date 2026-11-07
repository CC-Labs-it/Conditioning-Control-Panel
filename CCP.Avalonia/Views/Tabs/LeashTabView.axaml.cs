using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
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
    private readonly Dictionary<string, Control> _offers = new();

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
        var offers = _service?.Available == true ? _service.Snapshot.Offers : Array.Empty<LeashOffer>();
        var cards = new List<Control>();
        foreach (var offer in offers)
        {
            var key = offer.From.Id + "@" + offer.At.ToUnixTimeSeconds() + "@" + offer.Id;
            if (!_offers.TryGetValue(key, out var card))
                _offers[key] = card = OfferRow(offer);
            cards.Add(card);
        }
        foreach (var key in _offers.Keys.Except(offers.Select(o => o.From.Id + "@" + o.At.ToUnixTimeSeconds() + "@" + o.Id)).ToArray()) _offers.Remove(key);
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
        EmptyPanel.IsVisible = me == null && holding.Count == 0 && offers.Count == 0;
        // WPF LeashDrawerSection.NoteOnScreen: receipt only after the row is hosted and visible.
        if (IsEffectivelyVisible)
            foreach (var offer in offers) _service?.NoteShown(offer.Id);
    }

    // WPF LeashDrawerSection.OfferRow (:160-192): Look opens the one ask overlay (LeashSurfaces.ShowAsk).
    private Control OfferRow(LeashOffer offer)
    {
        var look = new Button { Content = new TextBlock { Text = Loc.Get("leash_offer_look") }, Tag = "leash-offer-look", Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        look.Click += (_, _) => (TopLevel.GetTopLevel(this) as MainShellWindow)?.OfferSurfaces?.ShowAsk(offer);
        var text = new TextBlock { Text = offer.From.Name + " " + Loc.Get("leash_ask_title"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { text, look } };
        Grid.SetColumn(look, 1);
        return new Border { Tag = "leash-offer-row:" + offer.From.Id, Child = grid, Background = new SolidColorBrush(Color.Parse("#24102F")),
            BorderBrush = new SolidColorBrush(Color.Parse("#B49552")), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 8, 8, 8), Margin = new Thickness(0, 4, 0, 4) };
    }
}
