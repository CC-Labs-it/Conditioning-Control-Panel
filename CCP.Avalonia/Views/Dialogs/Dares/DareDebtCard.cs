using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Dares;
using ConditioningControlPanel.Controls.Dares;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Dares;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs.Dares;

/// <summary>WPF Dialogs/Dares/DareDebtCard.cs: one card, three choices (Pay, Renounce, Close CCP). It never
/// starts anything on its own: a Lockdown stake walks through its consent prompts first, and only "Start it"
/// on the last one sends StartLockdown (:173-213). Panic closes it (PanicSurfaces "dares-cards"), and the
/// "dares" row cancels a start already on the wire.</summary>
public sealed class DareDebtCard : Window
{
    private static DareDebtCard? _open;
    internal static DareDebtCard? Open => _open;
    /// <summary>WPF :93: a debt card is on screen (the landing does not stack a second one).</summary>
    internal static bool IsUp => _open != null;

    /// <summary>Does this PC start CCP with the OS? Swappable for the suite.</summary>
    internal static Func<bool> RunsAtStartup { get; set; } = () => CoreSettings.Current.RunOnStartup;

    /// <summary>WPF :42-46: Close CCP takes the panel's own exit (refused in Lockdown).</summary>
    internal static Action CloseApp { get; set; } = () =>
    {
        try { Views.Windows.MainShellWindow.Current?.RequestExit(); }
        catch (Exception ex) { Serilog.Log.Warning(ex, "[Dares] close CCP failed"); }
    };

    private readonly IDaresService _svc;
    private readonly DareBet _bet;
    private readonly StackPanel _body;
    private readonly Queue<string> _consent = new();
    private bool _busy, _closed;
    /// <summary>The last Pay press that reached the service. Test seam.</summary>
    internal Task<DareActResult>? LastPay { get; private set; }

    internal static void CloseForPanic()
    {
        try { _open?.Close(); } catch (InvalidOperationException) { }
    }

    private DareDebtCard(IDaresService svc, DareBet bet)
    {
        _svc = svc;
        _bet = bet;
        _body = DareLook.Shell(this, 560, "dares-debt-card");
        Closed += (_, _) => { _closed = true; _consent.Clear(); };
        BuildCard();
    }

    /// <summary>The launch card on the startup ladder: the task ends when the card has closed.</summary>
    public static Task ShowModal(Window? owner, DareBet bet)
    {
        var w = Make(bet);
        if (w == null) return Task.CompletedTask;
        if (owner is { IsVisible: true }) return w.ShowDialog(owner);
        var done = new TaskCompletionSource();
        w.Closed += (_, _) => done.TrySetResult();
        w.Show();
        return done.Task;
    }

    private static DareDebtCard? Make(DareBet bet)
    {
        if (App.Dares is not { } svc) return null;
        CloseForPanic();
        var w = new DareDebtCard(svc, bet);
        _open = w;
        w.Closed += (_, _) => { if (ReferenceEquals(_open, w)) _open = null; };
        return w;
    }

    /// <summary>"You lost to Moth. You owe 20 min Lockdown".</summary>
    private void Frame(string title)
    {
        _body.Children.Clear();
        _body.Children.Add(DareLook.Words(title, 22));
        var stake = _bet.MyStake;
        var owed = DareSlipRules.StakeText(stake, stake.Kind == DareStakeKind.Dare ? stake.DareNumber : 0, Loc.Get);
        _body.Children.Add(DareLook.Words(Loc.GetF("dares_debt_lost", _bet.Friend.Name) + " " + owed, 20));
    }

    private void BuildCard()
    {
        Frame(Loc.Get("dares_debt_title"));
        if (_bet.Debt?.RetryUsed == true) _body.Children.Add(DareLook.Words(Loc.Get("dares_debt_retry"), 16, DareLook.WarnBrush, "dares-debt-retry"));
        _body.Children.Add(DareLook.Words(Loc.Get("dares_debt_soft"), 16, DareLook.MutedBrush, "dares-debt-soft"));
        var row = new WrapPanel { Margin = new(0, 14, 0, 0), Tag = "dares-debt-buttons" };
        row.Children.Add(DareLook.Act(Loc.Get(DareCardRules.PayKey(_bet.MyStake.Kind)), null, true, OnPay, "dares-pay"));
        row.Children.Add(DareLook.Act(Loc.Get("dares_renounce"), Loc.Get("dares_renounce_sub"), false, () => _ = ActAsync(DareOp.Renounce), "dares-renounce"));
        row.Children.Add(DareLook.Act(Loc.Get("dares_close_ccp"), Loc.Get("dares_close_ccp_sub"), false, OnCloseApp, "dares-close-ccp"));
        _body.Children.Add(row);
    }

    private void OnPay()
    {
        FriendsSfx.Click();
        _consent.Clear();
        foreach (var s in DareCardRules.ConsentSteps(_bet.MyStake.Kind, RunsAtStartup())) _consent.Enqueue(s);
        NextConsentOrPay();
    }

    /// <summary>WPF :159-191: the consent prompts one at a time; "Not now" goes back to the card.</summary>
    private void NextConsentOrPay()
    {
        if (_closed) return;
        if (_consent.Count == 0) { LastPay = ActAsync(DareCardRules.PayOp(_bet.MyStake.Kind)); return; }
        if (_consent.Dequeue() == "lockdown")
        {
            Frame(Loc.Get("dares_ld_title"));
            // WPF 13fe8883df :167-170: the real Lockdown turns the panic key off only when the player's own
            // Lockdown setting says so (Dares never forces it), so the consent says what will happen.
            var panicOff = CoreSettings.Current.LockdownDisablePanicKey;
            _body.Children.Add(DareLook.Words("• " + Loc.GetF(panicOff ? "dares_ld_length" : "dares_ld_length_panic", _bet.MyStake.Amount), 19, null, "dares-consent-lockdown"));
            _body.Children.Add(DareLook.Words("• " + Loc.Get("dares_ld_phrase"), 19));
            _body.Children.Add(DareLook.Words("• " + Loc.Get("dares_ld_early"), 19));
        }
        else
        {
            Frame(Loc.Get("dares_ld_startup_title"));
            _body.Children.Add(DareLook.Words(Loc.Get("dares_ld_startup_body"), 19, null, "dares-consent-startup"));
        }
        var row = new WrapPanel { Margin = new(0, 16, 0, 0) };
        row.Children.Add(DareLook.Act(Loc.Get("dares_ld_sure"), null, true, () => { FriendsSfx.Click(); NextConsentOrPay(); }, "dares-consent-yes"));
        row.Children.Add(DareLook.Act(Loc.Get("dares_not_now"), null, false, () => { FriendsSfx.Dismiss(); _consent.Clear(); BuildCard(); }, "dares-consent-no"));
        _body.Children.Add(row);
    }

    /// <summary>WPF :210-246: Pay, I did it, Start Lockdown or Renounce, worded. Internal for the suite.</summary>
    internal async Task<DareActResult> ActAsync(DareOp op)
    {
        if (_busy || _closed) return DareActResult.TryLater;
        _busy = true;
        DareActResult r;
        try { r = await _svc.ActAsync(_bet.Id, op); }
        catch { r = DareActResult.TryLater; }
        _busy = false;
        if (_closed) return r;
        if (r != DareActResult.Done)
        {
            FriendsSfx.Denied();
            BuildCard();
            _body.Children.Add(DareLook.Words(Loc.Get(DareCardRules.ActKey(r)), 17, DareLook.WarnBrush, "dares-debt-result"));
            return r;
        }
        // The Lockdown takes the screen now; the card steps out of its way at once.
        if (op == DareOp.StartLockdown) { Close(); return r; }
        FriendsSfx.Accepted();
        var key = op == DareOp.Renounce ? "dares_debt_renounced" : DareCardRules.PaidKey(_bet.MyStake.Kind);
        var text = key == "dares_debt_done" ? Loc.Get(key) : Loc.GetF(key, _bet.Friend.Name);
        Frame(Loc.Get("dares_debt_title"));
        _body.Children.Add(DareLook.Stamp(Loc.Get(op == DareOp.Renounce ? "dares_stamp_renounced" : "dares_stamp_paid"), grey: op == DareOp.Renounce));
        _body.Children.Add(DareLook.Words(text, 20, null, "dares-debt-result"));
        _body.Children.Add(DareLook.Act(Loc.Get("dares_close"), null, false, Close, "dares-done"));
        return r;
    }

    private void OnCloseApp()
    {
        Serilog.Log.Information("[Dares] debt card: Close CCP");
        Close();
        // After the dialog returns, so the ladder sees it gone before the exit runs.
        Dispatcher.UIThread.Post(() => CloseApp());
    }
}
