using System;
using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
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

        /// <summary>WPF MainWindow.Patreon.cs TierOnNoticePass: a repeat rise after the one-time celebration.</summary>
        internal void TierOnNoticePass(bool mayShow)
        {
            try
            {
                var settings = CoreSettings.Current;
                var current = CoreAccount.HasLabAccess ? 2 : CoreAccount.HasPremiumAccess ? 1 : 0;
                // ponytail: no authoritative server-answered signal on this head yet. Never record a cached drop.
                var verdict = Services.TierOnNoticeRule.Decide(settings.LastKnownEffectiveTier, current, mayRecordDrop: false);
                if (verdict.Record != settings.LastKnownEffectiveTier)
                {
                    settings.LastKnownEffectiveTier = verdict.Record;
                    CoreSettings.Save();
                }
                if (!verdict.Owed || !mayShow
                    || Services.TierCelebration.IsOwed(settings.SeenFeatureIntros, current, onRise: true)) return;
                var title = global::ConditioningControlPanel.Localization.Loc.Get(Services.TierOnNoticeRule.TitleKey(current));
                var body = global::ConditioningControlPanel.Localization.Loc.Get("tier_on_body");
                StartupLadder.PresentOrInbox(new Services.Startup.InboxItem
                {
                    Key = "tier-on:" + current,
                    Title = title,
                    Summary = body,
                    Open = () => InboxNoticeCard.Present(this, title, body, null, null),
                });
            }
            catch (Exception ex) { Log.Debug(ex, "Tier-on notice failed"); }
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
