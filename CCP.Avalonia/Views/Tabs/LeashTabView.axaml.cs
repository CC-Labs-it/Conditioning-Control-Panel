using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using F = ConditioningControlPanel.Avalonia.Views.Controls.FriendsDrawer;
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
        // WPF LeashTabView.xaml.cs:36: LeashLook.Help(LeashExplainRole.Leashed) in the HelpHost.
        HelpHost.Child = LeashExplainWindow.HelpButton("leashed", () => TopLevel.GetTopLevel(this) as Window);
        EmptyButton.Click += (_, _) => (TopLevel.GetTopLevel(this) as MainShellWindow)?.ShowTab("friends");
        // WPF Controls/Leash/LeashDrawerSection.cs:37-59: bind when hosted and release when detached.
        AttachedToVisualTree += (_, _) => { LeashHead.ServiceChanged += OnServiceChanged; OnServiceChanged(); };
        DetachedFromVisualTree += (_, _) => { LeashHead.ServiceChanged -= OnServiceChanged; Unsubscribe(); };
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
    private void OnServiceChanged()
    {
        if (!global::Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            global::Avalonia.Threading.Dispatcher.UIThread.Post(OnServiceChanged);
            return;
        }
        if (IsEffectivelyVisible && VisualRoot != null) OnShown();
    }
    private void OnSnapshot(LeashSnapshot _) => Render();

    private void Render()
    {
        MyLeash? me = null;
        var snap = LeashSnapshot.Empty;
        try { if (_service?.Available == true) snap = _service.Snapshot; me = snap.Me; } catch { }
        // WPF LeashDrawerSection.cs:113-150: keep each held card and its open sheet across polls.
        var holding = snap.Holding;
        var cards = new List<Control>();
        // WPF LeashDrawerSection.cs:118: offers precede the own card.
        foreach (var offer in snap.Offers) cards.Add(OfferRow(offer));
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
        EmptyPanel.IsVisible = cards.Count == 0;
        // WPF LeashDrawerSection.cs:94 NoteOnScreen: an offer is seen once its row is hosted and visible.
        if (IsEffectivelyVisible)
            foreach (var offer in snap.Offers) _service?.NoteShown(offer.Id);
    }

    // WPF LeashDrawerSection.cs:159-196: sender, copy and Look pill.
    private Control OfferRow(LeashOffer o)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var av = F.Avatar(o.From.Name, 34, null);
        av.Margin = new Thickness(0, 0, 8, 0);
        g.Children.Add(av);
        var t = new TextBlock { FontSize = 12.5, Foreground = F.Text, FontFamily = F.Display, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        t.Inlines = new InlineCollection
        {
            new Run(o.From.Name) { Foreground = F.Gold, FontWeight = FontWeight.SemiBold },
            new Run(" " + Loc.Get("leash_ask_title")),
        };
        Grid.SetColumn(t, 1);
        g.Children.Add(t);
        var look = F.Pill(Loc.Get("leash_offer_look"), F.Gold, LeashAskCard.Ink, "leash-offer-look", F.Gold);
        look.Margin = new Thickness(6, 0, 0, 0);
        look.Padding = new Thickness(12, 5);
        look.CornerRadius = new CornerRadius(10);
        look.Click += (_, _) => LeashAskWindow.Open(o, () => _service, TopLevel.GetTopLevel(this) as Window);
        Grid.SetColumn(look, 2);
        g.Children.Add(look);
        return new Border
        {
            Background = LeashAskCard.Card, BorderBrush = LeashAskCard.GoldDeep, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 8, 8, 8), Margin = new Thickness(0, 4, 0, 4),
            Tag = "leash-offer-row:" + o.From.Id, Child = g,
        };
    }
}
