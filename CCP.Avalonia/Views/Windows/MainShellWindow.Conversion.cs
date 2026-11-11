using System;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Startup;
using ConditioningControlPanel.Services.Startup.Conversion;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    // WPF MainWindow.Patreon.cs:1279-1281, Inbox.cs:156-240.
    private void InitializeLapseInbox()
    {
        void Reading() { if (Dispatcher.UIThread.CheckAccess()) MaybePostLapseRow(); else Dispatcher.UIThread.Post(MaybePostLapseRow); }
        void Focus(object? sender, EventArgs e) { MaybePostLapseRow(); AccountSeed.OnFocus(); }
        AccountSeed.TierReadingApplied += Reading;
        Activated += Focus;
        Closed += (_, _) => { AccountSeed.TierReadingApplied -= Reading; Activated -= Focus; };
    }

    internal void MaybePostLapseRow()
    {
        try { PostLapseRow(); }
        catch (Exception ex) { Serilog.Log.Warning(ex, "[Inbox] lapse row failed"); }
    }

    private void PostLapseRow()
    {
        if (CoreSettings.Service == null || !AccountSeed.ServerAnsweredThisRun) return;
        var s = CoreSettings.Current;
        var now = StartupLadder.Time.GetUtcNow().UtcDateTime;
        var reading = ConversionRules.DecideLapse(s.PatreonTier, s.ServerTierSeen, s.PatreonPremiumValidUntil,
            s.PatreonLabValidUntil, now, false);
        if (reading.Row == LapseRow.None || LiveCoversLapse(reading.Tier)) return;
        if (AccountSeed.Patreon?.GrantLooksDead == true || TierGate.ReconnectIsTheAnswer())
        {
            const string key = ConversionRules.KeyLapseReconnect;
            var today = ConversionRules.DayStamp(StartupLadder.Time.GetLocalNow().DateTime);
            if (!ConversionRules.Stamp(s.InboxStamps, key, today)) return;
            CoreSettings.Save();
            var title = Loc.Get("tiergate_denied_reconnect");
            StartupLadder.PresentOrInbox(new InboxItem
            {
                Key = "conv:" + key, Glyph = "🔑", Title = title,
                Open = () => Dialogs.InboxNoticeCard.Present(this, title, "", Loc.Get("tiergate_reconnect_action"),
                    () => { if (!LockdownActive) _ = ReconnectFromGate(); }),
            });
            return;
        }
        if (!ConversionRules.Stamp(s.InboxStamps, reading.Key, reading.Stamp)) return;
        CoreSettings.Save();
        bool prime = reading.Tier >= 2, closed = reading.Row == LapseRow.Closed;
        var until = reading.UntilUtc.ToLocalTime();
        var titleText = closed
            ? until.Date == StartupLadder.Time.GetLocalNow().Date
                ? Loc.Get(prime ? "conv_lapse_closed_prime_today" : "conv_lapse_closed_basic_today")
                : Loc.GetF(prime ? "conv_lapse_closed_prime" : "conv_lapse_closed_basic", until.ToString("ddd d MMM"))
            : reading.DaysLeft <= 1 ? Loc.Get(prime ? "conv_lapse_warn_prime_one" : "conv_lapse_warn_basic_one")
                : Loc.GetF(prime ? "conv_lapse_warn_prime" : "conv_lapse_warn_basic", reading.DaysLeft);
        var body = Loc.Get(closed ? "conv_lapse_closed_body" : "conv_lapse_warn_body");
        var primary = Loc.Get(closed ? (prime ? "conv_reopen_prime" : "conv_reopen_basic") : (prime ? "conv_keep_prime" : "conv_keep_basic"));
        var secondary = Loc.Get(closed ? "conv_leave_closed" : "conv_let_close");
        StartupLadder.PresentOrInbox(new InboxItem
        {
            Key = "conv:" + reading.Key, Glyph = "⏳", Title = titleText, Summary = body,
            Open = () => Dialogs.InboxNoticeCard.Present(this, titleText, body, primary,
                () => { AccountSeed.NoteCheckout(); Dialogs.VaultGateDialog.Launch(ConversionRules.PlanUrl(reading.Tier)); }, secondary),
        });
    }

    // WPF Inbox.cs:494: grace is not a live tier.
    private static bool LiveCoversLapse(int tier)
    {
        var patreon = AccountSeed.Patreon;
        var substar = AccountSeed.SubscribeStar;
        return patreon?.IsWhitelisted == true || patreon?.CurrentTier >= (tier >= 2 ? PatreonTier.Level2 : PatreonTier.Level1)
            || tier < 2 && (substar?.IsWhitelisted == true || substar?.CurrentTier >= PatreonTier.Level1);
    }
}
