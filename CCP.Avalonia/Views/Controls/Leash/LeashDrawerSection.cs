// PORTED from WPF Controls/Leash/LeashDrawerSection.cs: offer rows and the own card, shared by the
// Social > Leash page and the friends drawer popup (WPF LeashTabView.xaml.cs:32, FriendsDrawer.cs:52/:395).
// ponytail: holder cards (LeashHolderCard), the receipts repaint and the friend card's Offer chip are wire-views-tab-leash-b-next.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;
using F = ConditioningControlPanel.Avalonia.Views.Controls.FriendsDrawer;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;

/// <summary>An incoming offer ("Juno wants to hold your leash", Look opens the ask card), then the
/// leashed side's own card. Keeps its own card between repaints so its state survives a poll.</summary>
public sealed class LeashDrawerSection : StackPanel
{
    private readonly Func<ILeashService?> _resolve;
    private ILeashService? _svc;
    private LeashSelfCard? _self;
    private bool _subscribed;

    /// <summary>Raised after every repaint (the page swaps its empty state on it).</summary>
    public event Action? Changed;

    public LeashDrawerSection() : this(() => LeashHead.Service) { }

    internal LeashDrawerSection(Func<ILeashService?> service)
    {
        _resolve = service;
        Tag = "leash-section";
        // WPF Loaded/Unloaded. A host that hides it (the page) calls OnShown/Unsubscribe itself (P01).
        AttachedToVisualTree += (_, _) => { if (IsEffectivelyVisible) OnShown(); };
        DetachedFromVisualTree += (_, _) => Unsubscribe();
        Render();
    }

    internal ILeashService? Service => _svc;
    internal LeashSelfCard? SelfCard => _self;

    /// <summary>The host is on screen: pick up the current service, repaint, report what is seen.</summary>
    internal void OnShown()
    {
        Rebind();
        Render();
    }

    private void Rebind()
    {
        ILeashService? next = null;
        try { next = _resolve(); } catch { }
        if (ReferenceEquals(next, _svc) && _subscribed) return;
        Unsubscribe();
        _svc = next;
        if (_svc != null) { _svc.SnapshotChanged += OnSnapshot; _subscribed = true; }
    }

    internal void Unsubscribe()
    {
        if (_svc != null && _subscribed) _svc.SnapshotChanged -= OnSnapshot;
        _subscribed = false;
    }

    private void OnSnapshot(LeashSnapshot _)
    {
        try { Render(); } catch (Exception ex) { Serilog.Log.Debug("[Leash] section repaint failed: {E}", ex.Message); }
    }

    /// <summary>WPF NoteOnScreen: offer rows and today's task are seen here; a punishment is seen on the gate.</summary>
    private void NoteOnScreen(LeashSnapshot snap)
    {
        if (_svc is not { } s || !IsEffectivelyVisible) return;
        try
        {
            foreach (var o in snap.Offers) s.NoteShown(o.Id);
            if (snap.Me?.Assignment is { } a) s.NoteShown(a.Aid);
        }
        catch (Exception ex) { Serilog.Log.Debug("[Leash] seen note failed: {E}", ex.Message); }
    }

    private LeashSnapshot Snap()
    {
        try { return _svc?.Available == true ? _svc.Snapshot ?? LeashSnapshot.Empty : LeashSnapshot.Empty; }
        catch { return LeashSnapshot.Empty; }
    }

    internal void Render()
    {
        Children.Clear();
        var snap = Snap();
        foreach (var o in snap.Offers) Children.Add(OfferRow(o));
        if (snap.Me is { } me)
        {
            if (_self == null) _self = new LeashSelfCard(me, () => _svc);
            else _self.Update(me);
            Children.Add(_self);
        }
        else _self = null;
        IsVisible = Children.Count > 0;
        Changed?.Invoke();
        NoteOnScreen(snap);
    }

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
        look.Click += (_, _) => LeashAskWindow.Open(o, () => _svc, TopLevel.GetTopLevel(this) as Window);
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
