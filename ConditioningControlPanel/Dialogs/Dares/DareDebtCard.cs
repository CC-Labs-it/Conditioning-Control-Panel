using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Controls.Dares;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Dares;
using ConditioningControlPanel.Services.Friends;

namespace ConditioningControlPanel.Dialogs.Dares;

/// <summary>
/// THE DEBT CARD (DARES-HANDOFF "After the bet ends"): one card, a soft lock that says so, three
/// choices. Pay (its words by stake), Renounce (no dares for 7 days, the other side is told), Close
/// CCP (the card comes back next launch). It never starts anything on its own: a Lockdown stake walks
/// through its consent prompt first (and one more on a PC that starts CCP with Windows), then
/// <c>StartLockdown</c> runs the real Lockdown. At launch it is a modal on the startup ladder
/// (DaresLanding); from the strip it opens as a plain card.
/// </summary>
public sealed class DareDebtCard : Window
{
    private static DareDebtCard? _open;

    private readonly IDaresService _svc;
    private readonly DareBet _bet;
    private readonly Border _card;
    private readonly StackPanel _body = new();
    private readonly Grid _slip = new();
    private readonly Queue<string> _consent = new();
    private bool _busy;

    /// <summary>Does this PC start CCP with Windows? Swappable for the suite.</summary>
    internal static Func<bool> RunsAtStartup { get; set; } = () => App.Settings?.Current?.RunOnStartup == true;

    /// <summary>The way out the card's Close CCP takes: the panel's own exit (refused in Lockdown).</summary>
    internal static Action CloseApp { get; set; } = () =>
    {
        try { App.MainWindowRef?.RequestExit(); }
        catch (Exception ex) { App.Logger?.Warning(ex, "[Dares] close CCP failed"); }
    };

    private DareDebtCard(IDaresService svc, DareBet bet)
    {
        _svc = svc;
        _bet = bet;
        _card = DareLook.Shell(this, 520);
        _card.Tag = "dares-debt-card";
        BuildCard();
        DareLook.CardIn(_card);
    }

    /// <summary>The launch card: blocks until a choice is made (the ladder measures "done" by return).</summary>
    public static void ShowModal(Window? owner, DareBet bet)
    {
        var svc = App.Dares;
        if (svc == null) return;
        var w = Make(owner, svc, bet);
        try { w.ShowDialog(); }
        catch (InvalidOperationException ex) { App.Logger?.Debug("[Dares] debt card: {E}", ex.Message); }
    }

    /// <summary>The same card from the strip's Pay button.</summary>
    public static void Present(Window? owner, DareBet bet)
    {
        var svc = App.Dares;
        if (svc == null) return;
        Make(owner, svc, bet).Show();
    }

    private static DareDebtCard Make(Window? owner, IDaresService svc, DareBet bet)
    {
        try { _open?.Close(); } catch (InvalidOperationException) { } // swallow: already closing
        var w = new DareDebtCard(svc, bet);
        try { if (owner is { IsVisible: true }) w.Owner = owner; }
        catch (InvalidOperationException) { } // swallow: owner closing
        _open = w;
        w.Closed += (_, _) => { if (ReferenceEquals(_open, w)) _open = null; };
        return w;
    }

    /// <summary>A debt card is on screen (the landing does not stack a second one).</summary>
    internal static bool IsUp => _open != null;

    private void Frame(string title, string? help)
    {
        _body.Children.Clear();
        _body.Children.Add(DareLook.Head(title, help, null));
        _card.Child = _body;
    }

    private FrameworkElement OweLine()
    {
        var stake = _bet.MyStake;
        int n = stake.Kind == DareStakeKind.Dare ? stake.DareNumber : 0;
        var seg = new DareSeg(DareSegKind.Chip, DareSlipRules.StakeText(stake, n, Loc.Get), DareStep.Mine,
            DareSlipRules.ToneOf(stake.Kind), DareSlipRules.TipOf(stake.Kind));
        var row = new WrapPanel();
        row.Children.Add(DareLook.Words(Loc.GetF("dares_debt_lost", _bet.Friend.Name), 16, FriendsLook.TextBrush));
        row.Children.Add(DareLook.Chip(seg, null));
        var face = new Border
        {
            Background = FriendsLook.Frozen(FriendsLook.Rgb(0x23, 0x16, 0x40)),
            BorderBrush = FriendsLook.LineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 14, 16, 16),
            Child = row,
        };
        _slip.Children.Clear();
        _slip.Children.Add(face);
        return _slip;
    }

    private void BuildCard()
    {
        Frame(Loc.Get("dares_debt_title"), null);
        _body.Children.Add(OweLine());

        if (_bet.Debt?.RetryUsed == true)
            _body.Children.Add(Line(Loc.Get("dares_debt_retry"), FriendsLook.GoldBrush, "dares-debt-retry"));
        _body.Children.Add(Line(Loc.Get("dares_debt_soft"), FriendsLook.MutedBrush, "dares-debt-soft"));

        var row = new WrapPanel { Margin = new Thickness(0, 12, 0, 0), Tag = "dares-debt-buttons" };
        var kind = _bet.MyStake.Kind;
        row.Children.Add(DareLook.Big(Loc.Get(DareCardRules.PayKey(kind)), FriendsLook.Mint, OnPay, "dares-pay"));
        var renounce = new StackPanel { Orientation = Orientation.Horizontal };
        renounce.Children.Add(DareLook.Quiet(Loc.Get("dares_renounce"), Loc.Get("dares_renounce_sub"), () => _ = ActAsync(DareOp.Renounce), "dares-renounce"));
        var help = DareLook.Help("dares_help_renounce");
        help.Margin = new Thickness(-4, 0, 8, 8);
        renounce.Children.Add(help);
        row.Children.Add(renounce);
        row.Children.Add(DareLook.Quiet(Loc.Get("dares_close_ccp"), Loc.Get("dares_close_ccp_sub"), OnCloseApp, "dares-close-ccp"));
        _body.Children.Add(row);
    }

    private static TextBlock Line(string text, System.Windows.Media.Brush fg, string tag)
    {
        var t = DareLook.Words(text, 12.5, fg, wrap: true);
        t.Margin = new Thickness(0, 10, 0, 0);
        t.Tag = tag;
        return t;
    }

    private void OnPay()
    {
        FriendsSfx.Click();
        _consent.Clear();
        foreach (var s in DareCardRules.ConsentSteps(_bet.MyStake.Kind, RunsAtStartup())) _consent.Enqueue(s);
        NextConsentOrPay();
    }

    /// <summary>Walks the consent prompts one at a time; "Not now" goes back to the card.</summary>
    private void NextConsentOrPay()
    {
        if (_consent.Count == 0) { _ = ActAsync(DareCardRules.PayOp(_bet.MyStake.Kind)); return; }
        var step = _consent.Dequeue();
        if (step == "lockdown")
        {
            Frame(Loc.Get("dares_ld_title"), "dares_help_lockdown");
            var list = new StackPanel { Tag = "dares-consent-lockdown" };
            // The real Lockdown turns the panic key off only when the player's own Lockdown setting
            // says so (hard rule 6: Dares never forces it), so the consent says what will happen.
            var panicOff = App.Settings?.Current?.LockdownDisablePanicKey == true;
            list.Children.Add(Bullet(Loc.GetF(panicOff ? "dares_ld_length" : "dares_ld_length_panic", _bet.MyStake.Amount)));
            list.Children.Add(Bullet(Loc.Get("dares_ld_phrase")));
            list.Children.Add(Bullet(Loc.Get("dares_ld_early")));
            _body.Children.Add(list);
        }
        else
        {
            Frame(Loc.Get("dares_ld_startup_title"), null);
            var t = Line(Loc.Get("dares_ld_startup_body"), FriendsLook.TextBrush, "dares-consent-startup");
            _body.Children.Add(t);
        }
        var row = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        row.Children.Add(DareLook.Big(Loc.Get("dares_ld_sure"), FriendsLook.Mint, () => { FriendsSfx.Click(); NextConsentOrPay(); }, "dares-consent-yes"));
        row.Children.Add(DareLook.Quiet(Loc.Get("dares_not_now"), null, () => { FriendsSfx.Dismiss(); _consent.Clear(); BuildCard(); }, "dares-consent-no"));
        _body.Children.Add(row);
        DareLook.CardIn(_body);
    }

    private static FrameworkElement Bullet(string text)
    {
        var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var dot = DareLook.Glyph(DareLook.ToneGlyph(DareTone.Lockdown), 12, FriendsLook.Frozen(DareLook.ToneColor(DareTone.Lockdown)));
        dot.Margin = new Thickness(0, 0, 10, 0);
        g.Children.Add(dot);
        var t = DareLook.Words(text, 13.5, FriendsLook.TextBrush, wrap: true);
        Grid.SetColumn(t, 1);
        g.Children.Add(t);
        return g;
    }

    /// <summary>Pay, I did it, Start Lockdown or Renounce, worded. Internal for the suite.</summary>
    internal async Task<DareActResult> ActAsync(DareOp op)
    {
        if (_busy) return DareActResult.TryLater;
        _busy = true;
        DareActResult r;
        try { r = await _svc.ActAsync(_bet.Id, op); }
        catch { r = DareActResult.TryLater; }
        _busy = false;

        if (r != DareActResult.Done)
        {
            FriendsSfx.Denied();
            BuildCard();
            _body.Children.Add(Line(Loc.Get(DareCardRules.ActKey(r)), FriendsLook.GoldBrush, "dares-debt-result"));
            return r;
        }
        // The Lockdown takes the screen now; the card steps out of its way at once.
        if (op == DareOp.StartLockdown) { Close(); return r; }

        FriendsSfx.Accepted();
        string text;
        string stampWord;
        if (op == DareOp.Renounce)
        {
            text = Loc.GetF("dares_debt_renounced", _bet.Friend.Name);
            stampWord = Loc.Get("dares_stamp_renounced");
        }
        else
        {
            var key = DareCardRules.PaidKey(_bet.MyStake.Kind);
            text = key == "dares_debt_claimed" ? Loc.GetF(key, _bet.Friend.Name) : Loc.Get(key);
            stampWord = Loc.Get("dares_stamp_paid");
        }
        Frame(Loc.Get("dares_debt_title"), null);
        _body.Children.Add(OweLine());
        _slip.Children.Add(DareLook.Stamp(stampWord, op == DareOp.Renounce ? FriendsLook.Muted : FriendsLook.Mint));
        _body.Children.Add(Line(text, FriendsLook.MintBrush, "dares-debt-result"));
        var close = DareLook.Quiet(Loc.Get("dares_close"), null, () => DareLook.FadeOutThen(this, Close), "dares-done");
        close.Margin = new Thickness(0, 12, 0, 0);
        close.HorizontalAlignment = HorizontalAlignment.Left;
        _body.Children.Add(close);
        return r;
    }

    private void OnCloseApp()
    {
        App.Logger?.Information("[Dares] debt card: Close CCP");
        Close();
        // After the dialog returns, so the ladder sees it gone before the exit runs.
        Dispatcher.BeginInvoke(new Action(() => CloseApp()), System.Windows.Threading.DispatcherPriority.Normal);
    }
}
