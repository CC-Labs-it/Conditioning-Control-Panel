using System;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Startup;
using ConditioningControlPanel.Services.Startup.Conversion;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

/// <summary>
/// WPF MainWindow.Inbox.cs MaybePostLapseRow (proposals 4a, 4b; restack b160 from #4366): 7 / 3 / 1 days
/// before a grace end once the server has reported a lower tier, then one closed row; a dead Patreon grant
/// gets the Reconnect row instead. Never from memory alone: the server must have answered this run
/// (<see cref="EntitlementRefresh.ServerAnsweredThisRun"/>, set by <see cref="AccountSeed.ApplyTierReading"/>).
/// Each row is stamped in <c>AppSettings.InboxStamps</c> before it is handed to the presenter, so a focus
/// storm posts it once. No server call of its own: it reads what the profile, heartbeat and focus reads left.
/// </summary>
public partial class MainShellWindow
{
    /// <summary>WPF MainWindow.Patreon.cs:1378-1381: on focus, after a reading that moved the lower tier the
    /// server reported, and after the focus read's extras. The lapse pass (EnforceEntitlementLapse) posts too.</summary>
    private void InitializeLapseRows()
    {
        void Reading()
        {
            if (Dispatcher.UIThread.CheckAccess()) MaybePostLapseRow();
            else Dispatcher.UIThread.Post(MaybePostLapseRow);
        }
        void Extras() { if (IsVisible) MaybePostLapseRow(); }
        void Focused(object? sender, EventArgs e) => MaybePostLapseRow();
        AccountSeed.TierReadingApplied += Reading;
        AccountSeed.ProfileExtrasApplied += Extras;
        Activated += Focused;
        Closed += (_, _) =>
        {
            AccountSeed.TierReadingApplied -= Reading;
            AccountSeed.ProfileExtrasApplied -= Extras;
            Activated -= Focused;
        };
    }

    /// <summary>Never throws; saves only when a stamp changed.</summary>
    internal void MaybePostLapseRow()
    {
        try
        {
            if (CoreSettings.Service == null) return;
            if (PostLapseRow(CoreSettings.Current)) CoreSettings.Save();
        }
        catch (Exception ex) { Log.Warning(ex, "[Inbox] lapse row failed"); }
    }

    private bool PostLapseRow(AppSettings settings)
    {
        // Never from memory alone: the server must have answered this run (it may have just cleared the reading).
        if (!EntitlementRefresh.ServerAnsweredThisRun) return false;
        var now = StartupLadder.Time.GetUtcNow().UtcDateTime;
        var reading = ConversionRules.DecideLapseFor(settings, now);
        if (reading.Row == LapseRow.None || LiveCoversLapse(reading.Tier)) return false;
        var stamps = settings.InboxStamps;

        if (EntitlementTierRule.ClosedTierNow(settings, now) == null
            && (AccountSeed.Patreon?.GrantLooksDead == true || TierGate.ReconnectIsTheAnswer()))
        {
            const string reconnectKey = ConversionRules.KeyLapseReconnect;
            var today = ConversionRules.DayStamp(StartupLadder.Time.GetLocalNow().DateTime);
            if (!ConversionRules.Owed(stamps, reconnectKey, today)) return false;
            ConversionRules.Stamp(stamps, reconnectKey, today);
            var line = Loc.Get("tiergate_denied_reconnect");
            StartupLadder.PresentOrInbox(new InboxItem
            {
                Key = "conv:" + reconnectKey, Glyph = global::ConditioningControlPanel.Avalonia.Controls.IconMap.LapseReconnectKey, Title = line,
                Open = () => Dialogs.InboxNoticeCard.Present(this, line, "", Loc.Get("tiergate_reconnect_action"),
                    () => { if (!LockdownActive) _ = ReconnectFromGate(); }),
            });
            return true;
        }

        if (!ConversionRules.Owed(stamps, reading.Key, reading.Stamp)) return false;
        ConversionRules.Stamp(stamps, reading.Key, reading.Stamp);

        var prime = reading.Tier >= 2;
        var closed = reading.Row == LapseRow.Closed;
        var untilLocal = reading.UntilUtc.ToLocalTime();
        string title, body, primary, secondary;
        if (closed)
        {
            title = untilLocal.Date == StartupLadder.Time.GetLocalNow().Date
                ? Loc.Get(prime ? "conv_lapse_closed_prime_today" : "conv_lapse_closed_basic_today")
                : Loc.GetF(prime ? "conv_lapse_closed_prime" : "conv_lapse_closed_basic", untilLocal.ToString("ddd d MMM"));
            body = Loc.Get("conv_lapse_closed_body");
            primary = Loc.Get(prime ? "conv_reopen_prime" : "conv_reopen_basic");
            secondary = Loc.Get("conv_leave_closed");
        }
        else
        {
            title = reading.DaysLeft <= 1
                ? Loc.Get(prime ? "conv_lapse_warn_prime_one" : "conv_lapse_warn_basic_one")
                : Loc.GetF(prime ? "conv_lapse_warn_prime" : "conv_lapse_warn_basic", reading.DaysLeft);
            body = Loc.Get("conv_lapse_warn_body");
            primary = Loc.Get(prime ? "conv_keep_prime" : "conv_keep_basic");
            secondary = Loc.Get("conv_let_close");
        }

        var tier = reading.Tier;
        var rowKey = reading.Key;
        StartupLadder.PresentOrInbox(new InboxItem
        {
            Key = "conv:" + rowKey, Glyph = global::ConditioningControlPanel.Avalonia.Controls.IconMap.LapseKey, Title = title, Summary = body,
            // OpenPlanFromInbox goes through ExternalOpener, which arms the checkout watch (the return re-reads the tier).
            Open = () => Dialogs.InboxNoticeCard.Present(this, title, body, primary, () => OpenPlanFromInbox(tier, rowKey), secondary),
        });
        return true;
    }

    /// <summary>WPF MainWindow.Inbox.cs LiveCovers: something live (a validated Patreon tier, the whitelist,
    /// SubscribeStar for Basic) still holds <paramref name="tier"/>, so no lapse row. A grace stamp is not live.</summary>
    internal static bool LiveCoversLapse(int tier)
    {
        var patreon = AccountSeed.Patreon;
        if (patreon == null) return false;
        if (patreon.IsWhitelisted) return true;
        if (patreon.CurrentTier >= (tier >= 2 ? PatreonTier.Level2 : PatreonTier.Level1)) return true;
        var substar = AccountSeed.SubscribeStar;
        return tier < 2 && (substar?.IsWhitelisted == true || substar?.CurrentTier >= PatreonTier.Level1);
    }
}
