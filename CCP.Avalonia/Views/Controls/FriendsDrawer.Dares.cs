// PORTED from WPF 583d5e464d Controls/Friends/FriendsDrawer.Dares.cs: the Dares strip under the feed. Answer and Pay
// open the offer and debt cards (panic closes both via PanicSurfaces "dares-cards"); Tick, I did it, Cancel act in place.
// Part B: the menu's Dare door and its slip (WPF :39-64) come with s19-dares-slip-window.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.Dares;
using ConditioningControlPanel.Avalonia.Views.Dialogs.Dares;
using ConditioningControlPanel.Controls.Dares;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Dares;

namespace ConditioningControlPanel.Avalonia.Views.Controls;

public sealed partial class FriendsDrawer
{
    /// <summary>The Dares service this drawer draws (WPF :27). Swappable for the suite.</summary>
    internal static Func<IDaresService?> DaresSource { get; set; } = () => App.Dares;

    // WPF :29: one persistent panel refilled on Changed, so a ticking count never rebuilds the list under the pointer.
    private readonly StackPanel _daresBox = new() { Tag = "dares-strip", IsVisible = false };
    private IDaresService? _daresLive;

    private static DaresSnapshot DaresSnap() { try { return DaresSource()?.Snapshot ?? DaresSnapshot.Dark; } catch { return DaresSnapshot.Dark; } }
    private static IDaresService? DaresSvc() { try { return DaresSource(); } catch { return null; } }

    private void AddDares()
    {
        var svc = DaresSvc();
        if (!ReferenceEquals(svc, _daresLive))
        {
            UnhookDares();
            _daresLive = svc;
            if (_daresLive != null) _daresLive.Changed += OnDaresChanged;
        }
        (_daresBox.Parent as Panel)?.Children.Remove(_daresBox);
        _list.Children.Add(_daresBox);
        FillDares();
    }

    private void UnhookDares()
    {
        if (_daresLive != null) _daresLive.Changed -= OnDaresChanged;
        _daresLive = null;
    }

    private void OnDaresChanged()
    {
        try { if (IsVisible || AsPage) FillDares(); }
        catch (Exception ex) { Serilog.Log.Debug("[Dares] strip repaint failed: {E}", ex.Message); }
    }

    /// <summary>Bet ids in the strip, top to bottom. The suite reads it.</summary>
    internal IReadOnlyList<string> DareRowIds => _daresBox.Children.OfType<Control>()
        .Select(e => e.Tag as string).Where(t => t != null && t.StartsWith("dares-row:", StringComparison.Ordinal))
        .Select(t => t!.Substring("dares-row:".Length)).ToList();

    internal Control DaresStrip => _daresBox;

    private void FillDares()
    {
        _daresBox.Children.Clear();
        var snap = DaresSnap();
        var bets = DareCardRules.StripOrder(snap);
        bool banned = snap.Me.BannedUntil is DateTimeOffset until && until > ServerClock.UtcNow;
        _daresBox.IsVisible = snap.Available && (bets.Count > 0 || banned);
        if (!_daresBox.IsVisible) return;

        int open = bets.Count(b => DareCardRules.RowState(b) is DareRowState.Offer or DareRowState.IOwe or DareRowState.TheyOwe
            or DareRowState.Running or DareRowState.Writing or DareRowState.Waiting);
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(8, 10, 8, 4) };
        var n = Label(open.ToString(), 10.5, Dim, Mono);
        n.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(n, 1);
        head.Children.Add(Label(Loc.Get("dares_section").ToUpperInvariant(), 10.5, Dim, Mono, FontWeight.SemiBold));
        head.Children.Add(n);
        _daresBox.Children.Add(head);
        if (banned)
        {
            var b = Wrap(Label(Loc.GetF("dares_banned_until", snap.Me.BannedUntil!.Value.ToLocalTime().ToString("ddd d MMM HH:mm")), 11.5, Gold));
            (b.Margin, b.Tag) = (new Thickness(8, 0, 8, 4), "dares-banned");
            _daresBox.Children.Add(b);
        }
        bool preview = DaresSvc()?.IsPreview == true;
        foreach (var bet in bets) _daresBox.Children.Add(DareRow(bet, preview));
    }

    private Control DareRow(DareBet bet, bool preview)
    {
        var state = DareCardRules.RowState(bet);
        var row = new Border
        {
            CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 1),
            Background = state is DareRowState.Offer or DareRowState.IOwe ? new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0x5F, 0xB4)) : Brushes.Transparent,
            Tag = "dares-row:" + bet.Id,
        };
        ToolTip.SetTip(row, string.Join("\n", DareSlipRules.Sentence(bet, Loc.Get).Select(l => l.Plain)));
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("30,*,Auto") };
        var av = Avatar(bet.Friend.Name, 28, null);
        av.VerticalAlignment = VerticalAlignment.Top;
        g.Children.Add(av);

        var mid = new StackPanel { Margin = new Thickness(8, 0, 6, 0) };
        var top = new TextBlock { FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, FontFamily = Display };
        top.Inlines!.Add(new global::Avalonia.Controls.Documents.Run(bet.Friend.Name) { Foreground = Text, FontWeight = FontWeight.Medium });
        top.Inlines.Add(new global::Avalonia.Controls.Documents.Run(" · " + Loc.Get(DareCardRules.RowStateKey(state))) { Foreground = StateBrush(state) });
        if (bet.Debt?.EndedEarly == true)
            top.Inlines.Add(new global::Avalonia.Controls.Documents.Run(" · " + Loc.Get("dares_ended_early")) { Foreground = Gold });
        mid.Children.Add(top);

        // What it is about, short: the kind's icon and the "what" words.
        var what = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        var tone = DareSlipRules.ToneOf(bet.What.Kind);
        int.TryParse(bet.What.Kind == DareKind.Dare ? bet.What.Target : null, out var wn);
        what.Children.Add(new IconGlyph { Kind = DareLook.ToneKind(tone), Size = 11, Foreground = new SolidColorBrush(DareLook.ToneColor(tone)), Margin = new Thickness(0, 0, 5, 0) });
        what.Children.Add(Label(DareSlipRules.WhatText(bet.Type, bet.What, wn, Loc.Get), 11.5, Muted));
        mid.Children.Add(what);

        if (state == DareRowState.Running) mid.Children.Add(Progress(bet));
        if (state is DareRowState.IOwe or DareRowState.TheyOwe)
        {
            var stake = state == DareRowState.IOwe ? bet.MyStake : bet.TheirStake;
            var words = Loc.GetF(state == DareRowState.IOwe ? "dares_i_owe" : "dares_they_owe", DareSlipRules.StakeText(stake, stake.DareNumber, Loc.Get));
            var owe = Label(words, 11.5, new SolidColorBrush(DareLook.ToneColor(DareSlipRules.ToneOf(stake.Kind))));
            (owe.Margin, owe.Tag) = (new Thickness(0, 2, 0, 0), "dares-owe");
            mid.Children.Add(owe);
        }
        Grid.SetColumn(mid, 1);
        g.Children.Add(mid);

        var buttons = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        void Add(string key, IBrush bg, IBrush fg, Action act, string tag, string? tip = null)
        {
            var b = Pill(Loc.Get(key), bg, fg, tag, bg);
            (b.Padding, b.Margin) = (new Thickness(9, 3, 9, 3), new Thickness(0, 0, 0, 3));
            if (b.Content is TextBlock t) t.FontSize = 11.5;
            if (tip != null) ToolTip.SetTip(b, Loc.Get(tip));
            b.Click += (_, _) => { PlayLinkFx(b, false, Pink); act(); };
            buttons.Children.Add(b);
        }
        if (state == DareRowState.Offer)
            Add("dares_answer", Mint, MintInk, () => DareOfferCard.Present(OwnerWindow(), bet.Id), "dares-answer");
        // Pay opens the debt card: a Lockdown stake starts only after its consent there (WPF DareDebtCard.cs:173-213).
        if (state == DareRowState.IOwe && bet.Debt?.State == DareDebtState.Owed)
            Add(DareCardRules.PayKey(bet.MyStake.Kind), Mint, MintInk, () => DareDebtCard.Present(OwnerWindow(), bet), "dares-pay");
        if (DareCardRules.ShowsTick(bet))
            Add("dares_tick", Mint, MintInk, () => _ = DareActAsync(bet.Id, DareOp.Tick), "dares-tick", "dares_tick_tip");
        if (DareCardRules.ShowsDidIt(bet))
            Add("dares_pay_dare", Mint, MintInk, () => _ = DareActAsync(bet.Id, DareOp.DidIt), "dares-did-it");
        if (DareCardRules.ShowsCancel(bet))
            Add("dares_cancel", Raised, Text, () => _ = DareActAsync(bet.Id, DareOp.Cancel), "dares-cancel");
        if (DareCardRules.ShowsSettleNow(bet, preview))
            Add("dares_settle_now", Gold, new SolidColorBrush(Color.FromRgb(0x2A, 0x1C, 0x04)), () => _ = SettleNowAsync(bet.Id), "dares-settle-now", "dares_settle_now_tip");
        Grid.SetColumn(buttons, 2);
        g.Children.Add(buttons);
        row.Child = g;
        return row;
    }

    private static IBrush StateBrush(DareRowState s) => s switch
    {
        DareRowState.Offer or DareRowState.IOwe => Pink,
        DareRowState.Running or DareRowState.Won or DareRowState.Paid or DareRowState.TheyOwe => Mint,
        DareRowState.Lost or DareRowState.Renounced => Red,
        _ => Dim,
    };

    /// <summary>WPF :205: a running bet's bar(s) and time left. A "can't" has one bar, a duel both against the leader.</summary>
    private static Control Progress(DareBet bet)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 4, 0, 0), Tag = "dares-progress" };
        var c = DareLook.ToneColor(DareSlipRules.ToneOf(bet.What.Kind));
        if (bet.Type == DareType.Duel)
        {
            var (me, them) = DareCardRules.DuelBars(bet.MyCount, bet.TheirCount);
            sp.Children.Add(BarRow(Loc.Get("dares_mark_me"), me, bet.MyCount.ToString(), c));
            sp.Children.Add(BarRow(FriendsDrawerRules.Initials(bet.Friend.Name), them, bet.TheirCount.ToString(), ((ISolidColorBrush)Lilac).Color));
        }
        else if (DareCardRules.CountsUp(bet.What.Kind))
            sp.Children.Add(BarRow(null, DareCardRules.CantFraction(bet), DareCardRules.CantCount(bet) + " / " + bet.What.Target, c));
        else
            sp.Children.Add(Label(Loc.Get(bet.What.Kind == DareKind.Dare ? "dares_run_dare" : "dares_run_session"), 11, Dim));
        var (key, args) = DareCardRules.Left(bet.EndsAt - ServerClock.UtcNow);
        var left = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        left.Children.Add(new IconGlyph { Kind = IconKind.Clock, Size = 10, Foreground = Dim, Margin = new Thickness(0, 0, 4, 0) });
        left.Children.Add(Label(Loc.GetF(key, args), 10.5, Dim, Mono));
        if (bet.Camera)
        {
            var eye = DareLook.Icon(IconKind.Eye, 10, Mint);
            eye.Margin = new Thickness(8, 0, 0, 0);
            ToolTip.SetTip(eye, Loc.Get("dares_help_camera"));
            left.Children.Add(eye);
        }
        sp.Children.Add(left);
        return sp;
    }

    private static Control BarRow(string? label, double fraction, string count, Color c)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 1) };
        if (label != null) g.Children.Add(new TextBlock { Text = label, FontSize = 10, Foreground = Muted, FontFamily = Mono, Width = 24 });
        // WPF DareLook.Bar: a rounded track with the kind's colour filled to the fraction.
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions($"{fraction}*,{1 - fraction}*"), Height = 6, VerticalAlignment = VerticalAlignment.Center, Tag = "dares-bar" };
        bar.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), [Grid.ColumnSpanProperty] = 2 });
        bar.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(c) });
        Grid.SetColumn(bar, 1);
        g.Children.Add(bar);
        var n = Label(count, 10.5, Text, Mono);
        n.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(n, 2);
        g.Children.Add(n);
        return g;
    }

    /// <summary>WPF :262: one press on a strip row (Tick, I did it, Cancel), worded when it did not go. Internal for the suite.</summary>
    internal async Task<DareActResult> DareActAsync(string betId, DareOp op)
    {
        if (DaresSvc() is not { } svc) return DareActResult.Off;
        DareActResult r;
        try { r = await svc.ActAsync(betId, op); }
        catch { r = DareActResult.TryLater; }
        if (r == DareActResult.Done) Platform.FriendsSfx.Accepted();
        else
        {
            Platform.FriendsSfx.Denied();
            Say(Loc.Get(DareCardRules.ActKey(r)), false);
        }
        FillDares();
        return r;
    }

    private async Task SettleNowAsync(string betId)
    {
        if (DaresSvc() is not { IsPreview: true } svc) return;
        try { await svc.SettleNowAsync(betId); }
        catch (Exception ex) { Serilog.Log.Debug("[Dares] settle now: {E}", ex.Message); }
        FillDares();
    }
}
