using System;
using System.Linq;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Startup;
using ConditioningControlPanel.Services.Startup.Conversion;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    internal Func<string, bool> ConversionCheckout = ExternalOpener.Open;
    private void InitializeConversionRows()
    {
        // ponytail: heartbeat reading adoption and the tube's absent greeting host remain sync7-conversion-a-next.
        Opened += (_, _) => MaybePostConversionRows();
        Activated += (_, _) => { MaybePostConversionRows(); _ = AccountSeed.RefreshConversionAsync(); };
        void Applied() => Dispatcher.UIThread.Post(MaybePostConversionRows);
        AccountSeed.ConversionApplied += Applied;
        Closed += (_, _) => AccountSeed.ConversionApplied -= Applied;
    }

    internal void MaybePostConversionRows()
    {
        try
        {
            var s = CoreSettings.Current;
            var dirty = MaybePostLapseRow(s);
            dirty |= MaybePostMemberHello(s);
            if (dirty) CoreSettings.Save();
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Inbox conversion rows failed"); }
    }

    private bool MaybePostLapseRow(AppSettings s)
    {
        var reading = ConversionRules.DecideLapse(s.PatreonTier, s.ServerTierSeen,
            s.PatreonPremiumValidUntil, s.PatreonLabValidUntil, DateTime.UtcNow, false);
        if (reading.Row == LapseRow.None || LiveCovers(reading.Tier)) return false;
        if (AccountSeed.Patreon?.GrantLooksDead == true || TierGate.ReconnectIsTheAnswer())
        {
            var day = ConversionRules.DayStamp(DateTime.Now);
            if (!ConversionRules.Owed(s.InboxStamps, ConversionRules.KeyLapseReconnect, day)) return false;
            ConversionRules.Stamp(s.InboxStamps, ConversionRules.KeyLapseReconnect, day);
            var line = Loc.Get("tiergate_denied_reconnect");
            StartupLadder.PresentOrInbox(new InboxItem { Key = "conv:" + ConversionRules.KeyLapseReconnect, Title = line,
                Open = () => InboxNoticeCard.Present(this, line, "", Loc.Get("tiergate_reconnect_action"), () => _ = ReconnectFromGate()) });
            return true;
        }
        if (!ConversionRules.Owed(s.InboxStamps, reading.Key, reading.Stamp)) return false;
        ConversionRules.Stamp(s.InboxStamps, reading.Key, reading.Stamp);
        bool prime = reading.Tier >= 2, closed = reading.Row == LapseRow.Closed;
        var word = prime ? "prime" : "basic";
        string title;
        if (closed) title = reading.UntilUtc.ToLocalTime().Date == DateTime.Today
            ? Loc.Get("conv_lapse_closed_" + word + "_today")
            : Loc.GetF("conv_lapse_closed_" + word, reading.UntilUtc.ToLocalTime().ToString("ddd d MMM"));
        else title = reading.DaysLeft <= 1 ? Loc.Get("conv_lapse_warn_" + word + "_one")
            : Loc.GetF("conv_lapse_warn_" + word, reading.DaysLeft);
        var body = Loc.Get(closed ? "conv_lapse_closed_body" : "conv_lapse_warn_body");
        var primary = Loc.Get((closed ? "conv_reopen_" : "conv_keep_") + word);
        var secondary = Loc.Get(closed ? "conv_leave_closed" : "conv_let_close");
        StartupLadder.PresentOrInbox(new InboxItem { Key = "conv:" + reading.Key, Title = title, Summary = body,
            Open = () => InboxNoticeCard.Present(this, title, body, primary,
                () => ConversionCheckout(ConversionRules.PlanUrl(reading.Tier)), secondary) });
        return true;
    }

    internal static bool LiveCovers(int tier)
    {
        var p = AccountSeed.Patreon;
        if (p == null) return false;
        return p.IsWhitelisted || p.CurrentTier >= (tier >= 2 ? PatreonTier.Level2 : PatreonTier.Level1)
            || (tier < 2 && ProviderSubscription.HasPremiumAccess(null, AccountSeed.SubscribeStar, null));
    }

    private bool MaybePostMemberHello(AppSettings s)
    {
        const string key = ConversionRules.KeyFreeMemberHello;
        if (!s.PatreonMember || ProviderSubscription.HasPremiumAccess(AccountSeed.Patreon, AccountSeed.SubscribeStar, s)
            || !ConversionRules.Owed(s.InboxStamps, key, "1")) return false;
        ConversionRules.Stamp(s.InboxStamps, key, "1");
        var title = Loc.Get("conv_member_hello_title");
        var summary = Loc.Get("conv_member_hello_body");
        StartupLadder.PresentOrInbox(new InboxItem { Key = "conv:" + key, Title = title, Summary = summary, Open = () =>
        {
            var feature = ExclusiveFeature.All.FirstOrDefault(f => f.DailyFreeKey == App.DailyFree?.MemberKey);
            var body = feature == null ? summary : summary + " " + Loc.GetF("conv_member_second", Loc.Get(feature.TitleLocKey));
            InboxNoticeCard.Present(this, title, body, null, null, Loc.Get("conv_ok"));
        } });
        return true;
    }
}
