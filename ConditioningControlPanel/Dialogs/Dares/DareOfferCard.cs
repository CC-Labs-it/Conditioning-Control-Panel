using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ConditioningControlPanel.Controls.Dares;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Dares;
using ConditioningControlPanel.Services.Friends;

namespace ConditioningControlPanel.Dialogs.Dares;

/// <summary>
/// An offer, behind its Inbox row: the slip as it reads from my side, Accept or Pass. A Lockdown I
/// would owe states the consent here, at accept time (DARES-HANDOFF "Lockdown as a stake"). Nothing is
/// staked before Accept. A passive surface: it opens only when nothing owns the player (a session, a
/// game) and otherwise waits as a row (DaresLanding). After an Accept the "It's on" stamp lands and
/// the card steps out on its own a moment later, so it never sits under the next card.
/// </summary>
public sealed class DareOfferCard : Window, Services.Startup.IPassiveStartupSurface
{
    private static DareOfferCard? _open;

    /// <summary>How long an accepted card stays after its stamp lands.</summary>
    internal static readonly TimeSpan AcceptedLinger = TimeSpan.FromMilliseconds(1200);

    private readonly IDaresService _svc;
    private readonly string _betId;
    private readonly Border _card;
    private readonly StackPanel _body = new();
    private Grid _paper = new();
    private bool _busy;
    private bool _leaving;

    private DareOfferCard(IDaresService svc, DareBet bet)
    {
        _svc = svc;
        _betId = bet.Id;
        _card = DareLook.Shell(this, 640);
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
        var sentence = DareLook.Sentence(DareSlipRules.Sentence(bet, Loc.Get), bet.Friend.Name, null, null);
        var slip = DareLook.Slip(DareLook.SlipHead(Loc.GetF("dares_offer_title", bet.Friend.Name), "dares_help_numbers", Leave),
            sentence, out _paper);
        _body.Children.Add(slip);

        if (DareCardRules.OfferNeedsConsent(bet))
        {
            // Panic is off only when the player's own Lockdown setting says so (Dares never forces it).
            var panicOff = App.Settings?.Current?.LockdownDisablePanicKey == true;
            var row = Note("ldn", Loc.GetF(panicOff ? "dares_offer_consent" : "dares_offer_consent_panic", bet.MyStake.Amount), "dares_help_lockdown");
            row.Margin = new Thickness(0, 16, 0, 0);
            row.Tag = "dares-offer-consent";
            _body.Children.Add(row);
        }
        if (bet.Camera)
        {
            var cam = Note("eye", Loc.Get("dares_offer_camera"), "dares_help_camera");
            cam.Margin = new Thickness(0, 10, 0, 0);
            _body.Children.Add(cam);
        }

        var buttons = new WrapPanel { Margin = new Thickness(0, 18, 0, 0), Tag = "dares-offer-buttons" };
        buttons.Children.Add(DareLook.Big(Loc.Get("dares_accept"), DareBig.Go, () => _ = AnswerAsync(DareOp.Accept), "dares-accept"));
        buttons.Children.Add(DareLook.Quiet(Loc.Get("dares_pass"), null, () => _ = AnswerAsync(DareOp.Pass), "dares-pass"));
        _body.Children.Add(buttons);
        _card.Child = _body;
    }

    /// <summary>A line on the desk under the slip: an icon, the words, its (?).</summary>
    private static Grid Note(string icon, string text, string helpKey)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var i = DareLook.Icon(icon, 24, DareLook.ChalkBrush);
        i.VerticalAlignment = VerticalAlignment.Top;
        i.Margin = new Thickness(0, 1, 10, 0);
        g.Children.Add(i);
        var t = DareLook.Words(text, 18, DareLook.ChalkBrush, wrap: true);
        Grid.SetColumn(t, 1);
        g.Children.Add(t);
        var h = DareLook.Help(helpKey, DareLook.MutedBrush);
        h.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(h, 2);
        g.Children.Add(h);
        return g;
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
            _paper.Children.Add(DareLook.Stamp(Loc.Get(op == DareOp.Accept ? "dares_stamp_on" : "dares_stamp_passed"), grey: op != DareOp.Accept));
            Swap(Loc.Get(op == DareOp.Accept ? "dares_offer_on" : "dares_offer_passed"), true);
            if (op == DareOp.Accept) LeaveAfterStamp();
        }
        else
        {
            FriendsSfx.Denied();
            Swap(Loc.Get(DareCardRules.ActKey(r)), false);
        }
        return r;
    }

    /// <summary>The accepted card closes on its own: the stamp lands, a beat, then it fades out.</summary>
    private void LeaveAfterStamp()
    {
        var t = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(DareLook.StampLandMs) + AcceptedLinger,
        };
        t.Tick += (_, _) =>
        {
            t.Stop();
            if (IsVisible) Leave();
        };
        t.Start();
    }

    private void Swap(string line, bool good)
    {
        var buttons = _body.Children.OfType<FrameworkElement>().FirstOrDefault(e => (e.Tag as string) == "dares-offer-buttons");
        if (buttons != null) _body.Children.Remove(buttons);
        var t = DareLook.Words(line, 21, good ? DareLook.ChalkBrush : DareLook.WarnBrush, wrap: true);
        t.Margin = new Thickness(0, 18, 0, 10);
        t.Tag = "dares-offer-result";
        _body.Children.Add(t);
        var close = DareLook.Quiet(Loc.Get("dares_close"), null, Leave, "dares-done");
        _body.Children.Add(close);
    }

    private void Leave()
    {
        if (_leaving) return;
        _leaving = true;
        DareLook.FadeOutThen(this, Close);
    }
}
