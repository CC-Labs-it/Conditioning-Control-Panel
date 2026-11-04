using System;
using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Startup;
using ConditioningControlPanel.Services.Startup.Conversion;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// The title bar's half of the startup Inbox (WPF MainWindow/MainWindow.Inbox.cs): a glyph with a
    /// count, and the popup behind it. The list lives on <see cref="StartupLadder.Inbox"/>; this file
    /// only paints.
    /// </summary>
    public partial class MainShellWindow
    {
        private Popup? _inboxPopup;

        private void InitializeInboxBadge()
        {
            var inbox = StartupLadder.Inbox.Items;
            inbox.CollectionChanged += OnInboxCollectionChanged;
            Closed += (_, _) => inbox.CollectionChanged -= OnInboxCollectionChanged;
            RefreshInboxBadge();
        }

        private bool _lapseRowsClosed;

        // WPF MainWindow.Patreon.cs:1277-1280. The static event must not retain a closed shell.
        private void InitializeLapseRows()
        {
            AccountSeed.SubscriptionReadingApplied += OnLapseReadingApplied;
            Activated += OnLapseFocus;
            Closed += (_, _) =>
            {
                _lapseRowsClosed = true;
                AccountSeed.SubscriptionReadingApplied -= OnLapseReadingApplied;
                Activated -= OnLapseFocus;
            };
        }

        private void OnLapseFocus(object? sender, EventArgs e) => MaybePostLapseRow();

        private void OnLapseReadingApplied()
        {
            var settings = CoreSettings.Current;
            var id = settings.UnifiedId;
            var token = settings.AuthToken;
            var patreon = AccountSeed.Patreon;
            var generation = patreon?.SessionGeneration;
            void Apply()
            {
                if (_lapseRowsClosed || !ReferenceEquals(settings, CoreSettings.Current)
                    || settings.UnifiedId != id || settings.AuthToken != token
                    || !ReferenceEquals(patreon, AccountSeed.Patreon) || patreon?.SessionGeneration != generation) return;
                MaybePostLapseRow();
            }
            if (Dispatcher.UIThread.CheckAccess()) Apply();
            else Dispatcher.UIThread.Post(Apply);
        }

        // WPF MainWindow.Inbox.cs:175-239: a fresh lower reading, live coverage, reconnect,
        // one stamp per grace end, and the same passive presenter used by the other inbox rows.
        private void MaybePostLapseRow()
        {
            try
            {
                if (_lapseRowsClosed || !AccountSeed.EntitlementServerAnswered) return;
                var s = CoreSettings.Current;
                var reading = ConversionRules.DecideLapse(s.PatreonTier, s.ServerTierSeen,
                    s.PatreonPremiumValidUntil, s.PatreonLabValidUntil, DateTime.UtcNow, false);
                if (reading.Row == LapseRow.None || LiveCoversLapse(reading.Tier)) return;
                bool reconnect = AccountSeed.Patreon?.GrantLooksDead == true || TierGate.ReconnectIsTheAnswer();
                string key = reconnect ? ConversionRules.KeyLapseReconnect : reading.Key;
                string stamp = reconnect ? ConversionRules.DayStamp(DateTime.Now) : reading.Stamp;
                if (!ConversionRules.Owed(s.InboxStamps, key, stamp)) return;
                ConversionRules.Stamp(s.InboxStamps, key, stamp);
                CoreSettings.Save();

                bool prime = reading.Tier >= 2, closed = reading.Row == LapseRow.Closed;
                var localEnd = reading.UntilUtc.ToLocalTime();
                string title = reconnect ? Loc.Get("tiergate_denied_reconnect")
                    : closed ? (localEnd.Date == DateTime.Today
                        ? Loc.Get(prime ? "conv_lapse_closed_prime_today" : "conv_lapse_closed_basic_today")
                        : Loc.GetF(prime ? "conv_lapse_closed_prime" : "conv_lapse_closed_basic", localEnd.ToString("ddd d MMM")))
                    : reading.DaysLeft <= 1 ? Loc.Get(prime ? "conv_lapse_warn_prime_one" : "conv_lapse_warn_basic_one")
                    : Loc.GetF(prime ? "conv_lapse_warn_prime" : "conv_lapse_warn_basic", reading.DaysLeft);
                string body = reconnect ? "" : Loc.Get(closed ? "conv_lapse_closed_body" : "conv_lapse_warn_body");
                string primary = reconnect ? Loc.Get("tiergate_reconnect_action")
                    : Loc.Get(closed ? (prime ? "conv_reopen_prime" : "conv_reopen_basic") : (prime ? "conv_keep_prime" : "conv_keep_basic"));
                string? secondary = reconnect ? null : Loc.Get(closed ? "conv_leave_closed" : "conv_let_close");
                StartupLadder.PresentOrInbox(new InboxItem
                {
                    Key = "conv:" + key, Glyph = reconnect ? "🔑" : "⏳", Title = title, Summary = body,
                    Open = () => ShowLapseNotice(title, body, primary, secondary, reconnect, reading.Tier),
                });
            }
            catch (Exception ex) { Log.Warning(ex, "[Inbox] lapse row failed"); }
        }

        private static bool LiveCoversLapse(int tier)
        {
            var p = AccountSeed.Patreon;
            if (p == null) return false;
            if (p.IsWhitelisted || p.CurrentTier >= (tier >= 2 ? PatreonTier.Level2 : PatreonTier.Level1)) return true;
            var substar = AccountSeed.SubscribeStar;
            return tier < 2 && (substar?.CurrentTier >= PatreonTier.Level1 || substar?.IsWhitelisted == true);
        }

        private void ShowLapseNotice(string title, string body, string primary, string? secondary, bool reconnect, int tier)
        {
            Dialogs.InboxNoticeCard.Present(this, title, body, primary, secondary, () =>
            {
                if (reconnect) _ = ReconnectFromGate();
                else ExternalOpener.Open(ConversionRules.PlanUrl(tier));
            });
        }

        private void OnInboxCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(RefreshInboxBadge); return; }
            RefreshInboxBadge();
        }

        internal void RefreshInboxBadge()
        {
            try
            {
                var btn = Named<Button>("BtnInbox");
                if (btn == null) return;
                var count = StartupLadder.Inbox.UnreadCount;
                btn.Content = count > 99 ? "99+" : count.ToString();
                btn.IsVisible = count > 0;
                ToolTip.SetTip(btn, InboxFlyout.Str("inbox_tooltip", "Things that were waiting for a quieter moment"));
                if (count == 0) CloseInboxPopup();
            }
            catch (Exception ex) { Log.Debug("Inbox badge refresh: {E}", ex.Message); }
        }

        internal bool IsInboxOpen => _inboxPopup?.IsOpen == true;

        private void BtnInbox_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            try
            {
                if (_inboxPopup?.IsOpen == true) { CloseInboxPopup(); return; }
                var btn = Named<Button>("BtnInbox");
                if (btn == null) return;

                var flyout = new InboxFlyout();
                flyout.RequestClose += CloseInboxPopup;
                var popup = new Popup
                {
                    Child = flyout,
                    PlacementTarget = btn,
                    Placement = PlacementMode.Bottom,
                    HorizontalOffset = -300,
                    VerticalOffset = 6,
                    IsLightDismissEnabled = true,   // WPF StaysOpen=false
                    ShouldUseOverlayLayer = true,
                };
                ((ISetLogicalParent)popup).SetParent(btn);
                popup.Closed += (_, _) => { if (_inboxPopup == popup) _inboxPopup = null; };
                _inboxPopup = popup;
                popup.IsOpen = true;
            }
            catch (Exception ex) { Log.Warning(ex, "Inbox could not be opened"); }
        }

        /// <summary>WPF MainWindow.Inbox.cs:136 Summarise: one flattened line of a surface's body for an Inbox row.</summary>
        internal static string Summarise(string? body, int max = 90)
        {
            if (string.IsNullOrWhiteSpace(body)) return "";
            var flat = body.Replace("\r", " ").Replace("\n", " ").Trim();
            while (flat.Contains("  ")) flat = flat.Replace("  ", " ");
            return flat.Length <= max ? flat : flat.Substring(0, max - 1).TrimEnd() + "…";
        }

        private void CloseInboxPopup()
        {
            try
            {
                var popup = _inboxPopup;
                if (popup == null) return;
                _inboxPopup = null;
                popup.IsOpen = false;
            }
            catch (Exception ex) { Log.Debug("Inbox popup close: {E}", ex.Message); }
        }
    }
}
