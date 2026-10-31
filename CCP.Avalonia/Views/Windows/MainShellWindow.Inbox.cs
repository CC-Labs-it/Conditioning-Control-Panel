using System;
using System.Collections.Specialized;
using System.Linq;
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

        // WPF MainWindow.Inbox.cs:462-493. Never present this offer on the achievement toast.
        internal void PostPatronGalleryRow(Models.Achievement achievement)
        {
            if (achievement == null || achievement.IsExclusive || CoreAccount.HasPremiumAccess) return;
            if (CoreSettings.Service == null) return;
            var settings = CoreSettings.Current;
            var more = Models.Achievement.All.Values.Count(a => a.IsExclusive && !a.IsHidden);
            if (more <= 0) return;
            Dispatcher.UIThread.Post(() =>
            {
                const string key = "patron_gallery";
                var stamps = settings.InboxStamps;
                var today = Platform.StartupLadder.Time.GetLocalNow().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                if (stamps.TryGetValue(key, out var last) && last == today) return;
                stamps[key] = today;
                CoreSettings.Save();
                var title = global::ConditioningControlPanel.Localization.Loc.GetF("conv_gallery_title", more);
                var body = global::ConditioningControlPanel.Localization.Loc.Get("conv_gallery_body");
                StartupLadder.Inbox.File(new Services.Startup.InboxItem
                {
                    Key = "conv:" + key, Glyph = "🏅", Title = title, Summary = body,
                    Open = () => Dialogs.InboxNoticeCard.Present(this, title, body,
                        global::ConditioningControlPanel.Localization.Loc.Get("conv_get_basic"), () => Dialogs.VaultGateDialog.Launch(
                            "https://www.patreon.com/checkout/CodeBambi?rid=27604786")),
                });
            });
        }

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
