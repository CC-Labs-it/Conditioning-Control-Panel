using System;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Avalonia.Platform;
using Serilog;
namespace ConditioningControlPanel.Avalonia.Views.Windows;
public partial class MainShellWindow
{
        // ------------------------------------------------------------------ conversion rows (C9)

        /// <summary>
        /// Every conversion row this install owes right now, each through the presenter (so never
        /// inside a session, a game or the first-launch window) and each stamped in
        /// <c>AppSettings.InboxStamps</c> the moment it is handed over, so a focus storm posts it once.
        /// Called on focus, after a server tier reading, and after the lapse pass.
        /// </summary>
        internal void MaybePostConversionRows()
        {
            try
            {
                var settings = CoreSettings.Current;
                if (settings == null) return;
                var dirty = MaybePostLapseRow(settings);
                if (dirty) CoreSettings.Save();
            }
            catch (Exception ex) { Log.Warning(ex, "[Inbox] conversion rows failed"); }
        }

        /// <summary>The lapse rows (proposals 4a, 4b): 7 / 3 / 1 days before the grace end once the
        /// server has reported a lower tier, then one closed row. A dead Patreon grant gets the
        /// Reconnect copy instead, never a lapse row. True when a stamp changed.</summary>
        private bool MaybePostLapseRow(Models.AppSettings settings)
        {
            var reading = Services.Conversion.ConversionRules.DecideLapse(settings.PatreonTier, settings.ServerTierSeen,
                settings.PatreonPremiumValidUntil, settings.PatreonLabValidUntil, DateTime.UtcNow, liveCoversIt: false);
            if (reading.Row == Services.Conversion.LapseRow.None || LiveCovers(reading.Tier)) return false;
            var stamps = settings.InboxStamps;

            if (AccountSeed.Patreon?.GrantLooksDead == true || Services.TierGate.ReconnectIsTheAnswer())
            {
                var today = Services.Conversion.ConversionRules.DayStamp(DateTime.Now);
                const string reconnectKey = Services.Conversion.ConversionRules.KeyLapseReconnect;
                if (!Services.Conversion.ConversionRules.Owed(stamps, reconnectKey, today)) return false;
                Services.Conversion.ConversionRules.Stamp(stamps, reconnectKey, today);
                var line = Loc.Get("tiergate_denied_reconnect");
                StartupLadder.PresentOrInbox(new Services.Startup.InboxItem
                {
                    Key = "conv:" + reconnectKey,
                    Glyph = "🔑",
                    Title = line,
                    Open = () => Dialogs.InboxNoticeCard.Present(this, line, "", Loc.Get("tiergate_reconnect_action"),
                        () => { _ = ReconnectFromGate(); }),
                });
                return true;
            }

            if (!Services.Conversion.ConversionRules.Owed(stamps, reading.Key, reading.Stamp)) return false;
            Services.Conversion.ConversionRules.Stamp(stamps, reading.Key, reading.Stamp);

            var prime = reading.Tier >= 2;
            var closed = reading.Row == Services.Conversion.LapseRow.Closed;
            var untilLocal = reading.UntilUtc.ToLocalTime();
            string title, body, primary, secondary;
            if (closed)
            {
                title = untilLocal.Date == DateTime.Today
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
            StartupLadder.PresentOrInbox(new Services.Startup.InboxItem
            {
                Key = "conv:" + rowKey,
                Glyph = "⏳",
                Title = title,
                Summary = body,
                Open = () => Dialogs.InboxNoticeCard.Present(this, title, body, primary, () => OpenPlanFromInbox(tier, rowKey), secondary),
            });
            return true;
        }

        /// <summary>True when something live (a validated Patreon tier, the whitelist, SubscribeStar
        /// for Basic) still holds <paramref name="tier"/>: no lapse row then, whatever the stamps say.</summary>
        internal static bool LiveCovers(int tier)
        {
            var patreon = AccountSeed.Patreon;
            if (patreon == null) return false;
            if (patreon.IsWhitelisted) return true;
            if (patreon.CurrentTier >= (tier >= 2 ? Models.PatreonTier.Level2 : Models.PatreonTier.Level1)) return true;
            return tier < 2 && AccountSeed.SubscribeStar?.CurrentTier >= Models.PatreonTier.Level1;
        }

        /// <summary>A plan button on a conversion row: the Patreon checkout for the tier.</summary>
        internal static void OpenPlanFromInbox(int tier, string rowKey)
        {
            var url = Services.Conversion.ConversionRules.PlanUrl(tier);
            Log.Information("[Inbox] plan button on {Row} ({Plan})", rowKey, Services.Conversion.ConversionRules.PlanWord(tier));
            // TODO(D1): FunnelBeacon.Post("cta_clicked", rowKey, "patreon") once Services/Funnel lands on main.
            ExternalOpener.Open(url);
        }

}
