using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Controls.Dares;
using ConditioningControlPanel.Dialogs.Dares;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Dares;
using ConditioningControlPanel.Services.Friends;

namespace ConditioningControlPanel.Controls.Friends;

/// <summary>
/// DARES in the drawer: the door ("Dare" at the top of a friend's menu, right-click or "more") and
/// the strip under the feed that lists open bets (offers, debts, running with their bars and time
/// left, sent) and the last few settled ones. The strip is one persistent panel refilled when
/// <c>App.Dares</c> changes, so a count ticking up never rebuilds the friend list under the pointer.
/// While Dares is dark (flag off, old server, signed out) neither shows.
/// </summary>
public sealed partial class FriendsDrawer
{
    /// <summary>The Dares service this drawer draws. Swappable for the suite.</summary>
    internal static Func<IDaresService?> DaresSource { get; set; } = () => App.Dares;

    private readonly StackPanel _daresBox = new() { Tag = "dares-strip", Visibility = Visibility.Collapsed };
    private IDaresService? _daresLive;

    private DaresSnapshot DaresSnap()
    {
        try { return DaresSource()?.Snapshot ?? DaresSnapshot.Dark; } catch { return DaresSnapshot.Dark; }
    }

    private bool DaresOpen() => DaresSnap().Available;

    /// <summary>"Dare" leads the menu in the house pink; shut, with the date, while a renounce ban runs.</summary>
    private void DressDareItem(MenuItem mi)
    {
        mi.Foreground = FriendsLook.PinkBrush;
        mi.FontWeight = FontWeights.SemiBold;
        mi.Icon = DareLook.Icon("envelope", 18, FriendsLook.PinkBrush, null, 1.9);
        var snap = DaresSnap();
        if (!DareCardRules.CanDare(snap, ServerClock.UtcNow) && snap.Me.BannedUntil is DateTimeOffset until)
        {
            mi.IsEnabled = false;
            ToolTipService.SetShowOnDisabled(mi, true);
            mi.ToolTip = Loc.GetF("dares_banned_until", until.ToLocalTime().ToString("ddd d MMM HH:mm"));
        }
        else mi.ToolTip = Loc.Get("dares_menu_tip");
    }

    /// <summary>Opens the slip with this friend filled in. Internal for the suite.</summary>
    internal void OpenDareSlip(Friend f)
    {
        FriendsSfx.Click();
        var friends = _last.Friends.Select(x => new DareFriend(x.Id, x.Name, x.AvatarUrl)).ToList();
        var tiers = _last.Friends.GroupBy(x => x.Id).ToDictionary(x => x.Key, x => x.First().Tier);
        DareSlipWindow.Open(Window.GetWindow(this) ?? App.MainWindowRef, new DareFriend(f.Id, f.Name, f.AvatarUrl), friends, tiers);
        CloseRequested?.Invoke();
    }

    private void AddDares(FriendsSnapshot snap)
    {
        var svc = DaresSource();
        if (!ReferenceEquals(svc, _daresLive))
        {
            if (_daresLive != null) _daresLive.Changed -= OnDaresChanged;
            _daresLive = svc;
            if (_daresLive != null) _daresLive.Changed += OnDaresChanged;
        }
        _list.Children.Add(_daresBox);
        FillDares();
    }

    private void OnDaresChanged()
    {
        try { if (IsVisible || AsPage) FillDares(); }
        catch (Exception ex) { App.Logger?.Debug("[Dares] strip repaint failed: {E}", ex.Message); }
    }

    /// <summary>Bet ids in the strip, top to bottom. The suite reads it.</summary>
    internal IReadOnlyList<string> DareRowIds => _daresBox.Children.OfType<FrameworkElement>()
        .Select(e => e.Tag as string).Where(t => t != null && t.StartsWith("dares-row:", StringComparison.Ordinal))
        .Select(t => t!.Substring("dares-row:".Length)).ToList();

    internal FrameworkElement DaresStrip => _daresBox;

    private void FillDares()
    {
        _daresBox.Children.Clear();
        var snap = DaresSnap();
        var bets = DareCardRules.StripOrder(snap);
        bool banned = snap.Me.BannedUntil is DateTimeOffset until && until > ServerClock.UtcNow;
        if (!snap.Available || (bets.Count == 0 && !banned)) { _daresBox.Visibility = Visibility.Collapsed; return; }
        _daresBox.Visibility = Visibility.Visible;

        int open = bets.Count(b => DareCardRules.RowState(b) is DareRowState.Offer or DareRowState.IOwe or DareRowState.TheyOwe
            or DareRowState.Running or DareRowState.Writing or DareRowState.Waiting);
        var head = FriendsLook.SectionHead(Loc.Get("dares_section"), open);
        _daresBox.Children.Add(head);
        if (banned)
        {
            var b = FriendsLook.Label(Loc.GetF("dares_banned_until", snap.Me.BannedUntil!.Value.ToLocalTime().ToString("ddd d MMM HH:mm")),
                11.5, FriendsLook.GoldBrush, FriendsLook.Display);
            b.TextWrapping = TextWrapping.Wrap;
            b.TextTrimming = TextTrimming.None;
            b.Margin = new Thickness(8, 0, 8, 4);
            b.Tag = "dares-banned";
            _daresBox.Children.Add(b);
        }
        bool preview = false;
        try { preview = DaresSource()?.IsPreview == true; } catch { }
        foreach (var bet in bets) _daresBox.Children.Add(DareRow(bet, preview));
    }

    private FrameworkElement DareRow(DareBet bet, bool preview)
    {
        var state = DareCardRules.RowState(bet);
        var row = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 1, 0, 1),
            Background = state is DareRowState.Offer or DareRowState.IOwe ? DareLook.Tint(FriendsLook.Pink, 0x18) : Brushes.Transparent,
            Tag = "dares-row:" + bet.Id,
            ToolTip = string.Join("\n", DareSlipRules.Sentence(bet, Loc.Get).Select(l => l.Plain)),
        };
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var av = FriendsLook.Avatar(bet.Friend.Name, bet.Friend.AvatarUrl, 28);
        av.VerticalAlignment = VerticalAlignment.Top;
        g.Children.Add(av);

        var mid = new StackPanel { Margin = new Thickness(8, 0, 6, 0) };
        var top = new TextBlock { FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, FontFamily = FriendsLook.Display };
        top.Inlines.Add(new System.Windows.Documents.Run(bet.Friend.Name) { Foreground = FriendsLook.TextBrush, FontWeight = FontWeights.Medium });
        top.Inlines.Add(new System.Windows.Documents.Run(" · " + Loc.Get(DareCardRules.RowStateKey(state)))
        { Foreground = StateBrush(state) });
        if (bet.Debt?.EndedEarly == true)
            top.Inlines.Add(new System.Windows.Documents.Run(" · " + Loc.Get("dares_ended_early")) { Foreground = FriendsLook.GoldBrush });
        mid.Children.Add(top);

        // What it is about, short: a small handwritten fill, the kind's icon and colour.
        var tone = DareSlipRules.ToneOf(bet.What.Kind);
        int.TryParse(bet.What.Kind == DareKind.Dare ? bet.What.Target : null, out var wn);
        var what = DareLook.Fill(new DareSeg(DareSegKind.Chip, DareSlipRules.WhatText(bet.Type, bet.What, wn, Loc.Get), DareStep.What,
            tone, DareSlipRules.TipOf(bet.What.Kind)), null, small: true);
        what.Margin = new Thickness(0, 4, 0, 0);
        mid.Children.Add(what);

        if (state == DareRowState.Running) mid.Children.Add(Progress(bet));
        if (state is DareRowState.IOwe or DareRowState.TheyOwe)
        {
            var stake = state == DareRowState.IOwe ? bet.MyStake : bet.TheirStake;
            mid.Children.Add(OweRow(Loc.Get(state == DareRowState.IOwe ? "dares_i_owe" : "dares_they_owe"),
                new DareSeg(DareSegKind.Chip, DareSlipRules.StakeText(stake, stake.DareNumber, Loc.Get), DareStep.Mine,
                    DareSlipRules.ToneOf(stake.Kind), DareSlipRules.TipOf(stake.Kind))));
        }
        Grid.SetColumn(mid, 1);
        g.Children.Add(mid);

        var buttons = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        void Add(string key, Brush bg, Brush fg, Action act, string tag, string? tip = null)
        {
            var b = FriendsLook.Pill(Loc.Get(key), bg, fg, bg, 8, new Thickness(9, 3, 9, 3), bg);
            b.FontSize = 11.5;
            b.Margin = new Thickness(0, 0, 0, 3);
            b.Tag = tag;
            if (tip != null) b.ToolTip = Loc.Get(tip);
            b.Click += (_, _) => { Pop(b, FriendsLook.Pink); act(); };
            buttons.Children.Add(b);
        }
        var owner = Window.GetWindow(this) ?? App.MainWindowRef;
        if (state == DareRowState.Offer)
            Add("dares_answer", FriendsLook.MintBrush, FriendsLook.MintInkBrush, () => DareOfferCard.Present(owner, bet.Id), "dares-answer");
        if (state == DareRowState.IOwe && bet.Debt?.State == DareDebtState.Owed)
            Add(DareCardRules.PayKey(bet.MyStake.Kind), FriendsLook.MintBrush, FriendsLook.MintInkBrush, () => DareDebtCard.Present(owner, bet), "dares-pay");
        if (DareCardRules.ShowsTick(bet))
            Add("dares_tick", FriendsLook.MintBrush, FriendsLook.MintInkBrush, () => _ = DareActAsync(bet.Id, DareOp.Tick), "dares-tick", "dares_tick_tip");
        if (DareCardRules.ShowsDidIt(bet))
            Add("dares_pay_dare", FriendsLook.MintBrush, FriendsLook.MintInkBrush, () => _ = DareActAsync(bet.Id, DareOp.DidIt), "dares-did-it");
        if (DareCardRules.ShowsCancel(bet))
            Add("dares_cancel", FriendsLook.RaisedBrush, FriendsLook.TextBrush, () => _ = DareActAsync(bet.Id, DareOp.Cancel), "dares-cancel");
        if (DareCardRules.ShowsSettleNow(bet, preview))
            Add("dares_settle_now", FriendsLook.GoldBrush, FriendsLook.Frozen(FriendsLook.Rgb(0x2A, 0x1C, 0x04)), () => _ = SettleNowAsync(bet.Id), "dares-settle-now", "dares_settle_now_tip");
        Grid.SetColumn(buttons, 2);
        g.Children.Add(buttons);
        row.Child = g;
        AttachSheen(row, false);
        return row;
    }

    /// <summary>"I owe [fill]": the template's words around a small handwritten fill of the stake.</summary>
    private static FrameworkElement OweRow(string template, DareSeg stake)
    {
        var row = new WrapPanel { Margin = new Thickness(0, 4, 0, 0), Tag = "dares-owe" };
        int at = template.IndexOf("{0}", StringComparison.Ordinal);
        string before = at < 0 ? template : template.Substring(0, at), after = at < 0 ? "" : template.Substring(at + 3);
        if (before.Trim().Length > 0)
        {
            var b = FriendsLook.Label(before.Trim(), 11.5, FriendsLook.TextBrush, FriendsLook.Display);
            b.Margin = new Thickness(0, 0, 5, 0);
            row.Children.Add(b);
        }
        row.Children.Add(DareLook.Fill(stake, null, small: true));
        if (after.Trim().Length > 0)
        {
            var a = FriendsLook.Label(after.Trim(), 11.5, FriendsLook.TextBrush, FriendsLook.Display);
            a.Margin = new Thickness(5, 0, 0, 0);
            row.Children.Add(a);
        }
        return row;
    }

    private static Brush StateBrush(DareRowState s) => s switch
    {
        DareRowState.Offer or DareRowState.IOwe => FriendsLook.PinkBrush,
        DareRowState.Running or DareRowState.Won or DareRowState.Paid or DareRowState.TheyOwe => FriendsLook.MintBrush,
        DareRowState.Lost or DareRowState.Renounced => FriendsLook.RedBrush,
        _ => FriendsLook.DimBrush,
    };

    /// <summary>A running bet: the bar(s) and the time left. A "can't" has one bar (the receiver's
    /// count against the target); a duel has both, against the leader.</summary>
    private FrameworkElement Progress(DareBet bet)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 4, 0, 0), Tag = "dares-progress" };
        var c = DareLook.ToneColor(DareSlipRules.ToneOf(bet.What.Kind));
        if (bet.Type == DareType.Duel)
        {
            var (me, them) = DareCardRules.DuelBars(bet.MyCount, bet.TheirCount);
            sp.Children.Add(BarRow(Loc.Get("dares_mark_me"), me, bet.MyCount.ToString(), c));
            sp.Children.Add(BarRow(FriendsDrawerRules.Initials(bet.Friend.Name), them, bet.TheirCount.ToString(), FriendsLook.Lilac));
        }
        else if (DareCardRules.CountsUp(bet.What.Kind))
        {
            sp.Children.Add(BarRow(null, DareCardRules.CantFraction(bet), DareCardRules.CantCount(bet) + " / " + bet.What.Target, c));
        }
        else
        {
            sp.Children.Add(FriendsLook.Label(Loc.Get(bet.What.Kind == DareKind.Dare ? "dares_run_dare" : "dares_run_session"), 11, FriendsLook.DimBrush));
        }
        var (key, args) = DareCardRules.Left(bet.EndsAt - ServerClock.UtcNow);
        var left = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        var clock = DareLook.Icon("clock", 15, FriendsLook.MutedBrush);
        clock.Margin = new Thickness(0, 0, 4, 0);
        left.Children.Add(clock);
        left.Children.Add(FriendsLook.Label(Loc.GetF(key, args), 10.5, FriendsLook.DimBrush, FriendsLook.Mono));
        if (bet.Camera)
        {
            var eye = DareLook.Icon("eye", 16, FriendsLook.MintBrush);
            eye.Margin = new Thickness(8, 0, 0, 0);
            eye.IsHitTestVisible = true;
            eye.ToolTip = Loc.Get("dares_help_camera");
            System.Windows.Automation.AutomationProperties.SetName(eye, Loc.Get("dares_help_camera"));
            left.Children.Add(eye);
        }
        sp.Children.Add(left);
        return sp;
    }

    private static FrameworkElement BarRow(string? label, double fraction, string count, Color c)
    {
        var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (label != null)
        {
            var l = FriendsLook.Label(label, 10, FriendsLook.MutedBrush, FriendsLook.Mono);
            l.Width = 24;
            g.Children.Add(l);
        }
        var bar = DareLook.Bar(fraction, c, 6);
        Grid.SetColumn(bar, 1);
        g.Children.Add(bar);
        var n = FriendsLook.Label(count, 10.5, FriendsLook.TextBrush, FriendsLook.Mono);
        n.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(n, 2);
        g.Children.Add(n);
        return g;
    }

    /// <summary>One press on a strip row (Tick, I did it, Cancel), worded when it did not go. Internal for the suite.</summary>
    internal async Task<DareActResult> DareActAsync(string betId, DareOp op)
    {
        var svc = DaresSource();
        if (svc == null) return DareActResult.Off;
        DareActResult r;
        try { r = await svc.ActAsync(betId, op); }
        catch { r = DareActResult.TryLater; }
        if (r == DareActResult.Done) FriendsSfx.Accepted();
        else
        {
            FriendsSfx.Denied();
            TellOutside(Loc.Get(DareCardRules.ActKey(r)), false, always: true);
        }
        FillDares();
        return r;
    }

    private async Task SettleNowAsync(string betId)
    {
        var svc = DaresSource();
        if (svc?.IsPreview != true) return;
        try { await svc.SettleNowAsync(betId); }
        catch (Exception ex) { App.Logger?.Debug("[Dares] settle now: {E}", ex.Message); }
        FillDares();
    }
}
