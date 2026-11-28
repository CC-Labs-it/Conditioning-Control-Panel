using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Dares;
using ConditioningControlPanel.Controls.Dares;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Dares;
using ConditioningControlPanel.Services.Startup;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs.Dares;

/// <summary>WPF Dialogs/Dares/DareOfferCard.cs: an offer behind its Inbox row, Accept or Pass. A Lockdown
/// I would owe states its consent here (:80-88); nothing is staked before Accept. Panic closes it
/// (PanicSurfaces "dares-cards").</summary>
public sealed class DareOfferCard : Window, IPassiveStartupSurface
{
    private static DareOfferCard? _open;
    internal static DareOfferCard? Open => _open;

    /// <summary>WPF :26: how long an accepted card stays after its stamp lands.</summary>
    internal static readonly TimeSpan AcceptedLinger = TimeSpan.FromMilliseconds(1200);

    private readonly IDaresService _svc;
    private readonly string _betId;
    private readonly StackPanel _body;
    private bool _busy, _closed;

    internal static void CloseForPanic()
    {
        try { _open?.Close(); } catch (InvalidOperationException) { }
    }

    private DareOfferCard(IDaresService svc, DareBet bet)
    {
        _svc = svc;
        _betId = bet.Id;
        _body = DareLook.Shell(this, 560, "dares-offer-card");
        Closed += (_, _) => _closed = true;
        // WPF :42-43: the first-time introduction comes before the first offer.
        if (DareIntro.Seen()) Build(bet);
        else _body.Children.Add(DareIntro.Build(() => { _body.Children.Clear(); Build(Find(_svc, _betId) ?? bet); }));
    }

    public static void Present(Window? owner, string betId)
    {
        var svc = App.Dares;
        var bet = Find(svc, betId);
        if (svc == null || bet == null || !bet.IsOfferToMe) return;
        CloseForPanic();
        var w = new DareOfferCard(svc, bet);
        _open = w;
        w.Closed += (_, _) => { if (ReferenceEquals(_open, w)) _open = null; };
        try { if (owner is { IsVisible: true }) w.Show(owner); else w.Show(); }
        catch (InvalidOperationException) { w.Show(); }
    }

    internal static DareBet? Find(IDaresService? svc, string id)
    {
        try { return svc?.Snapshot.Bets.FirstOrDefault(b => b.Id == id); } catch { return null; }
    }

    private void Build(DareBet bet)
    {
        _body.Children.Add(DareLook.Words(Loc.GetF("dares_offer_title", bet.Friend.Name), 22));
        foreach (var line in DareSlipRules.Sentence(bet, Loc.Get)) _body.Children.Add(DareLook.Words(line.Plain, 18));
        if (DareCardRules.OfferNeedsConsent(bet))
        {
            // WPF 13fe8883df :83-85: panic is off only when the player's own Lockdown setting says so.
            var panicOff = CoreSettings.Current.LockdownDisablePanicKey;
            _body.Children.Add(DareLook.Words(Loc.GetF(panicOff ? "dares_offer_consent" : "dares_offer_consent_panic", bet.MyStake.Amount),
                17, null, "dares-offer-consent"));
        }
        if (bet.Camera) _body.Children.Add(DareLook.Words(Loc.Get("dares_offer_camera"), 17));
        var buttons = new WrapPanel { Margin = new(0, 18, 0, 0), Tag = "dares-offer-buttons" };
        buttons.Children.Add(DareLook.Act(Loc.Get("dares_accept"), null, true, () => _ = AnswerAsync(DareOp.Accept), "dares-accept"));
        buttons.Children.Add(DareLook.Act(Loc.Get("dares_pass"), null, false, () => _ = AnswerAsync(DareOp.Pass), "dares-pass"));
        _body.Children.Add(buttons);
    }

    /// <summary>WPF :119-149: Accept or Pass, worded. Internal for the suite.</summary>
    internal async Task<DareActResult> AnswerAsync(DareOp op)
    {
        if (_busy || _closed) return DareActResult.TryLater;
        // A Sparkles stake I cannot cover is worded here, never sent.
        if (op == DareOp.Accept && Find(_svc, _betId) is { } bet && !DareCardRules.CanCover(bet, _svc.Sparkles))
        {
            FriendsSfx.Denied();
            if (!_body.Children.Any(c => c.Tag as string == "dares-offer-short"))
                _body.Children.Insert(_body.Children.Count - 1, DareLook.Words(Loc.Get(DareCardRules.ActKey(DareActResult.Short)), 18, DareLook.WarnBrush, "dares-offer-short"));
            if (_body.Children[^1] is Panel row) row.Children[0].IsEnabled = false;   // WPF :174-179: Pass stays, Accept goes quiet
            return DareActResult.Short;
        }
        _busy = true;
        DareActResult r;
        try { r = await _svc.ActAsync(_betId, op); }
        catch { r = DareActResult.TryLater; }
        _busy = false;
        StartupLadder.Inbox.Remove(Platform.DaresLanding.OfferKey(_betId));
        if (_closed) return r;
        _body.Children.Remove(_body.Children.First(c => c.Tag as string == "dares-offer-buttons"));
        if (r == DareActResult.Done)
        {
            if (op == DareOp.Accept) FriendsSfx.Accepted(); else FriendsSfx.Dismiss();
            _body.Children.Add(DareLook.Stamp(Loc.Get(op == DareOp.Accept ? "dares_stamp_on" : "dares_stamp_passed"), grey: op != DareOp.Accept));
            _body.Children.Add(DareLook.Words(Loc.Get(op == DareOp.Accept ? "dares_offer_on" : "dares_offer_passed"), 20, null, "dares-offer-result"));
            if (op == DareOp.Accept) DispatcherTimer.RunOnce(Close, AcceptedLinger);
        }
        else
        {
            FriendsSfx.Denied();
            _body.Children.Add(DareLook.Words(Loc.Get(DareCardRules.ActKey(r)), 20, DareLook.WarnBrush, "dares-offer-result"));
        }
        _body.Children.Add(DareLook.Act(Loc.Get("dares_close"), null, false, Close, "dares-done"));
        return r;
    }
}
