using System;
using System.Linq;
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
/// An offer, behind its Inbox row: the slip as it reads from my side, Accept or Pass. A Lockdown I
/// would owe states the consent here, at accept time (DARES-HANDOFF "Lockdown as a stake"). Nothing is
/// staked before Accept. A passive surface: it opens only when nothing owns the player (a session, a
/// game) and otherwise waits as a row (DaresLanding).
/// </summary>
public sealed class DareOfferCard : Window, Services.Startup.IPassiveStartupSurface
{
    private static DareOfferCard? _open;

    private readonly IDaresService _svc;
    private readonly string _betId;
    private readonly Border _card;
    private readonly StackPanel _body = new();
    private readonly Grid _slip = new();
    private bool _busy;

    private DareOfferCard(IDaresService svc, DareBet bet)
    {
        _svc = svc;
        _betId = bet.Id;
        _card = DareLook.Shell(this, 520);
        _card.Tag = "dares-offer-card";
        if (DareIntro.Seen()) Build(bet);
        else _card.Child = DareIntro.Build(() => Build(Current() ?? bet));
        DareLook.CardIn(_card);
    }

    public static void Present(Window? owner, string betId)
    {
        var svc = App.Dares;
        var bet = Find(svc, betId);
        if (svc == null || bet == null || !bet.IsOfferToMe) return;
        try { _open?.Close(); } catch (InvalidOperationException) { } // swallow: already closing
        var w = new DareOfferCard(svc, bet);
        try { if (owner is { IsVisible: true }) w.Owner = owner; }
        catch (InvalidOperationException) { } // swallow: owner closing
        _open = w;
        w.Closed += (_, _) => { if (ReferenceEquals(_open, w)) _open = null; };
        w.Show();
    }

    internal static DareBet? Find(IDaresService? svc, string id)
    {
        try { return svc?.Snapshot.Bets.FirstOrDefault(b => b.Id == id); } catch { return null; }
    }

    private DareBet? Current() => Find(_svc, _betId);

    private void Build(DareBet bet)
    {
        _body.Children.Clear();
        _body.Children.Add(DareLook.Head(Loc.GetF("dares_offer_title", bet.Friend.Name), "dares_help_numbers", Leave));
        var face = new Border
        {
            Background = FriendsLook.Frozen(FriendsLook.Rgb(0x23, 0x16, 0x40)),
            BorderBrush = FriendsLook.LineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 14, 16, 16),
            Child = DareLook.Sentence(DareSlipRules.Sentence(bet, Loc.Get), bet.Friend.Name, null, null),
        };
        _slip.Children.Clear();
        _slip.Children.Add(face);
        _body.Children.Add(_slip);

        if (DareCardRules.OfferNeedsConsent(bet))
        {
            var consent = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0), Tag = "dares-offer-consent" };
            var icon = DareLook.Glyph(DareLook.ToneGlyph(DareTone.Lockdown), 14, FriendsLook.Frozen(DareLook.ToneColor(DareTone.Lockdown)));
            icon.Margin = new Thickness(0, 0, 8, 0);
            consent.Children.Add(icon);
            var t = DareLook.Words(Loc.GetF("dares_offer_consent", bet.MyStake.Amount), 12.5, FriendsLook.TextBrush, wrap: true);
            t.MaxWidth = 400;
            consent.Children.Add(t);
            consent.Children.Add(DareLook.Help("dares_help_lockdown"));
            _body.Children.Add(consent);
        }
        if (bet.Camera)
        {
            var cam = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            cam.Children.Add(DareLook.Words(Loc.Get("dares_offer_camera"), 12, FriendsLook.MutedBrush));
            cam.Children.Add(DareLook.Help("dares_help_camera"));
            _body.Children.Add(cam);
        }

        var row = new WrapPanel { Margin = new Thickness(0, 14, 0, 0), Tag = "dares-offer-buttons" };
        row.Children.Add(DareLook.Big(Loc.Get("dares_accept"), FriendsLook.Mint, () => _ = AnswerAsync(DareOp.Accept), "dares-accept"));
        row.Children.Add(DareLook.Quiet(Loc.Get("dares_pass"), null, () => _ = AnswerAsync(DareOp.Pass), "dares-pass"));
        _body.Children.Add(row);
        _card.Child = _body;
    }

    /// <summary>Accept or Pass, worded. Internal for the suite.</summary>
    internal async Task<DareActResult> AnswerAsync(DareOp op)
    {
        if (_busy) return DareActResult.TryLater;
        _busy = true;
        DareActResult r;
        try { r = await _svc.ActAsync(_betId, op); }
        catch { r = DareActResult.TryLater; }
        _busy = false;
        App.StartupLadder?.RemoveRow(DaresLanding.OfferKey(_betId));
        if (r == DareActResult.Done)
        {
            if (op == DareOp.Accept) FriendsSfx.Accepted(); else FriendsSfx.Dismiss();
            var stamp = DareLook.Stamp(Loc.Get(op == DareOp.Accept ? "dares_stamp_on" : "dares_stamp_passed"),
                op == DareOp.Accept ? FriendsLook.Mint : FriendsLook.Muted);
            _slip.Children.Add(stamp);
            Swap(Loc.Get(op == DareOp.Accept ? "dares_offer_on" : "dares_offer_passed"), true);
        }
        else
        {
            FriendsSfx.Denied();
            Swap(Loc.Get(DareCardRules.ActKey(r)), false);
        }
        return r;
    }

    private void Swap(string line, bool good)
    {
        var buttons = _body.Children.OfType<FrameworkElement>().FirstOrDefault(e => (e.Tag as string) == "dares-offer-buttons");
        if (buttons != null) _body.Children.Remove(buttons);
        var t = DareLook.Words(line, 13, good ? FriendsLook.MintBrush : FriendsLook.GoldBrush, wrap: true);
        t.Margin = new Thickness(0, 14, 0, 8);
        t.Tag = "dares-offer-result";
        _body.Children.Add(t);
        var close = DareLook.Quiet(Loc.Get("dares_close"), null, Leave, "dares-done");
        close.HorizontalAlignment = HorizontalAlignment.Left;
        _body.Children.Add(close);
    }

    private void Leave() => DareLook.FadeOutThen(this, Close);
}
