using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Dialogs.Dares;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Controls.Dares;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Dares;
using ConditioningControlPanel.Services.Startup;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF Controls/Dares/DaresLanding.cs: an offer goes through the passive route (opens, or waits as an
/// Inbox row); a debt goes on the modal ladder once a launch, never while a session or a Lockdown runs.
/// The plan is <see cref="DareCardRules.LandingPlan"/>. Started from the shell's Inbox (WPF MainWindow.Inbox.cs:29-33).</summary>
internal static class DaresLanding
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
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        Tick();
    }

    public static void Stop()
    {
        _timer?.Stop();
        _timer = null;
        if (_svc != null) _svc.Changed -= OnChanged;
        _svc = null;
    }

    /// <summary>One pass: re-attach to the current service, then post what is new. Internal for the suite.</summary>
    internal static void Tick()
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
        catch (Exception ex) { Serilog.Log.Debug("[Dares] landing tick: {E}", ex.Message); }
    }

    private static bool Holding()
    {
        try { return CoreSession.IsSessionRunning || LockdownService.Current?.IsActive == true; }
        catch { return true; }
    }

    private static void OnChanged()
    {
        if (_svc is not { } svc) return;
        DaresSnapshot snap;
        try { snap = svc.Snapshot; } catch { return; }
        var (post, gone, _) = DareCardRules.LandingPlan(snap, Posted, DebtShown, Holding());
        foreach (var id in gone)
        {
            Posted.Remove(id);
            StartupLadder.Inbox.Remove(OfferKey(id));
        }
        foreach (var bet in post)
        {
            Posted.Add(bet.Id);
            var id = bet.Id;
            StartupLadder.PresentOrInbox(new InboxItem
            {
                Key = OfferKey(id),
                Title = Loc.GetF("dares_offer_title", bet.Friend.Name),
                Summary = DareSlipRules.Sentence(bet, Loc.Get).FirstOrDefault()?.Plain ?? "",
                Glyph = "\U0001F3B2",
                Open = () => DareOfferCard.Present(MainShellWindow.Current, id),
            });
        }
    }

    /// <summary>Test seam: forget what was posted and shown.</summary>
    internal static void ResetForTests()
    {
        Stop();
        Posted.Clear();
        DebtShown.Clear();
    }
}
