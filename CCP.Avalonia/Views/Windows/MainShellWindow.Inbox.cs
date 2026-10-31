using System;
using System.Collections.Specialized;
using System.Linq;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Startup;
using ConditioningControlPanel.Services.Startup.Conversion;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
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
            var poll = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            poll.Tick += async (_, _) => await CheckPostCode();
            Opened += async (_, _) => { poll.Start(); await CheckPostCode(); };
            Closed += (_, _) => { _postCodeClosed = true; poll.Stop(); };
            var inbox = StartupLadder.Inbox.Items;
            inbox.CollectionChanged += OnInboxCollectionChanged;
            Closed += (_, _) => inbox.CollectionChanged -= OnInboxCollectionChanged;
            RefreshInboxBadge();
        }

        private readonly PostCodeFlow _postCodeFlow = new();
        private bool _postCodeClosed;

        private async System.Threading.Tasks.Task CheckPostCode()
        {
            try
            {
                var settings = CoreSettings.Current;
                var account = settings.UnifiedId;
                var live = await _postCodeFlow.CheckAsync(settings, DateTime.UtcNow, () => PostCodeApi.LiveAsync());
                if (_postCodeClosed || !ReferenceEquals(settings, CoreSettings.Current)
                    || settings.UnifiedId != account || settings.OfflineMode || !settings.PatreonMember) return;
                var info = ExclusiveFeature.All.FirstOrDefault(f => f.DailyFreeKey == live?.Feature);
                var held = info?.Tier >= 2 ? CoreAccount.HasLabAccess : CoreAccount.HasPremiumAccess;
                if (!PostCodeFlow.StampRow(settings, live, held, DateTime.Now)) return;
                CoreSettings.Save();
                StartupLadder.Inbox.File(new InboxItem
                {
                    Key = "conv:" + PostCodeFlow.RowKey, Glyph = "🔖", Title = Loc.Get("conv_postcode_title"),
                    Summary = Loc.GetF("conv_postcode_body", info == null ? live!.Feature! : Loc.Get(info.TitleLocKey)),
                    Open = () => PostCodeDialog.Present(this),
                });
            }
            catch (Exception ex) { Log.Debug(ex, "Post code live check failed"); }
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
