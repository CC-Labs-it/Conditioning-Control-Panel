using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using ConditioningControlPanel.Dialogs.Dares;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Dares;

namespace ConditioningControlPanel.Controls.Dares;

/// <summary>
/// Where Dares reach the player outside the drawer. An offer goes through the startup presenter's
/// passive route (<c>PresentOrInbox</c>): it opens when nothing owns the player and waits as an Inbox
/// row while a session or a game does, like every other delivery. A debt goes on the modal ladder
/// (<c>EnqueueModal</c>), once a launch, never while a session or a Lockdown runs; the card itself
/// never starts anything. The plan is <see cref="DareCardRules.LandingPlan"/>.
///
/// <para><see cref="Start"/> is called once from the panel (MainWindow.Inbox). <c>App.Dares</c> may be
/// null and may be rebuilt on sign in / out, so a light timer re-attaches to the current one.</para>
/// </summary>
public static class DaresLanding
{
    private const int DebtPriority = 45;

    private static DispatcherTimer? _timer;
    private static IDaresService? _svc;
    private static readonly HashSet<string> Posted = new(StringComparer.Ordinal);
    private static readonly HashSet<string> DebtShown = new(StringComparer.Ordinal);

    public static string OfferKey(string betId) => "dares-offer:" + betId;

    public static void Start()
    {
        if (_timer != null) return;
        _timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(3) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        Tick();
    }

    public static void Stop()
    {
        try { _timer?.Stop(); } catch { /* shutting down */ }
        _timer = null;
        if (_svc != null) _svc.Changed -= OnChanged;
        _svc = null;
    }

    private static void Tick()
    {
        try
        {
            var current = App.Dares;
            if (!ReferenceEquals(current, _svc))
            {
                if (_svc != null) _svc.Changed -= OnChanged;
                _svc = current;
                if (_svc != null) _svc.Changed += OnChanged;
            }
            OnChanged();
        }
        catch (Exception ex) { App.Logger?.Debug("[Dares] landing tick: {E}", ex.Message); }
    }

    private static bool Holding()
    {
        try { return App.IsSessionRunning || App.Lockdown?.IsActive == true; }
        catch { return true; }
    }

    private static void OnChanged()
    {
        var svc = _svc;
        if (svc == null) return;
        DaresSnapshot snap;
        try { snap = svc.Snapshot; } catch { return; }
        var (post, gone, debt) = DareCardRules.LandingPlan(snap, Posted, DebtShown, Holding() || DareDebtCard.IsUp);
        var ladder = App.StartupLadder;

        foreach (var id in gone)
        {
            Posted.Remove(id);
            ladder?.RemoveRow(OfferKey(id));
        }
        foreach (var bet in post)
        {
            Posted.Add(bet.Id);
            var id = bet.Id;
            var item = new Services.Startup.InboxItem
            {
                Key = OfferKey(id),
                Title = Loc.GetF("dares_offer_title", bet.Friend.Name),
                Summary = DareSlipRules.Sentence(bet, Loc.Get).FirstOrDefault()?.Plain ?? "",
                Glyph = "\U0001F3B2",
                Open = () => DareOfferCard.Present(App.MainWindowRef, id),
            };
            if (ladder != null) ladder.PresentOrInbox(item);
            else item.Open();
        }
        if (debt != null)
        {
            DebtShown.Add(debt.Id);
            var id = debt.Id;
            void Show(Window? owner)
            {
                // Read again at its turn: it may have been paid from the strip while it waited.
                var now = DareOfferCard.Find(App.Dares, id);
                if (now is { IOwe: true, Debt.State: DareDebtState.Owed }) DareDebtCard.ShowModal(owner ?? App.MainWindowRef, now);
            }
            if (ladder != null) ladder.EnqueueModal("dares-debt:" + id, DebtPriority, Show);
            else Show(App.MainWindowRef);
        }
    }
}
