// PORTED-IN-PART from ConditioningControlPanel/MainWindow/MainWindow.Browser.cs.
// Navigation, offline gating, profile clearing, tray restoration and the HTTPS-only missing-engine
// escape hatch are live. WebHost also has a script channel and the card reapplies its mute script.
// Missing: native engine mute (cross-origin frames), crash/init retry, document-created scripts,
// media/audio-sync reporting, remote video takeover, autoplay, pop-out and fullscreen/mirroring.
// Profile account/art work belongs to its separate ledger rows. Site-radio mod sync lives on the card.

using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using Serilog;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // ============================== the embedded browser ==============================

        private const string BambiCloudHome = "https://bambicloud.com/";
        private const string HypnoTubeHome = "https://hypnotube.com/";

        /// <summary>The Settings tab, which hosts the browser card. Resolved on every read, and
        /// its controls reached with FindControl: this window loads with AvaloniaXamlLoader.Load,
        /// so its generated fields are never assigned (MainShellWindow.TabNavigation.cs).</summary>
        private Tabs.SettingsTabView? BrowserPage => Named<Tabs.SettingsTabView>("SettingsTab");

        private WebHost? BrowserView => BrowserPage?.FindControl<WebHost>("BrowserWebHost");

        /// <summary>Homepage of whichever site radio is selected. BambiCloud is the default, as on
        /// WPF, including the "both deselected after an external link" case.</summary>
        private string SiteHomeUrl() =>
            BrowserPage?.FindControl<RadioButton>("RbHypnoTube")?.IsChecked == true
                ? HypnoTubeHome : BambiCloudHome;

        /// <summary>The offline gate, checked FIRST by every entry point below - #867 moved it
        /// above the lazy-init branch on WPF because underneath it the first click still built a
        /// browser and loaded a page, the one thing the block exists to prevent.</summary>
        /// <param name="userInitiated">WPF toasts only for navigation the user asked for; so does
        /// this, and it picks the log level.</param>
        private static bool BrowserBlockedOffline(bool userInitiated = true)
        {
            if (!CoreSettings.Current.OfflineMode) return false;
            if (userInitiated)
            {
                Log.Information("Browser action blocked by offline mode");
                App.Notifications.Show(Loc.Get("browser_toast_offline_blocked"),
                    Helpers.NotificationType.Warning, TimeSpan.FromSeconds(6));
            }
            else Log.Debug("Browser navigation blocked by offline mode");
            return true;
        }

        /// <summary>Points the web view at a URL. A missing host (the tab is not in the tree, as in
        /// a single-view render) is a no-op that returns false, never a throw.</summary>
        private bool NavigateBrowser(string url)
        {
            try
            {
                var host = BrowserView;
                if (host == null) return false;
                host.Navigate(new Uri(url));   // even when equal: WPF #867, re-click goes home
                Log.Information("Browser navigated to {Url}", url);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Browser navigation failed for {Url}", url);
                return false;
            }
        }

        /// <summary>WPF's <c>_browser.NavigationCompleted</c> handler (MainWindow.Browser.cs:132-160),
        /// minus the unported audio-sync service injection. Forwarded by
        /// SettingsTabView from its WebHost with the LIVE url, so a link clicked inside the page
        /// is looked up too, not just the URL we asked for.</summary>
        internal void OnBrowserNavigationCompleted(Uri url)
        {
            if (BrowserPage?.FindControl<TextBlock>("TxtBrowserStatus") is { } status)
            {
                // Bound, not assigned: a local Text under {loc:Str} is undone on a language change.
                status.Bind(TextBlock.TextProperty, new global::Avalonia.Data.Binding("[label_connected_2]")
                    { Source = LocalizationManager.Instance, Mode = global::Avalonia.Data.BindingMode.OneWay });
                status.Foreground = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.FromRgb(0, 230, 118));
            }
            TriggerCatalogueLookupForNavigation(url.AbsoluteUri);   // escaped, like CoreWebView2.Source
        }

        /// <summary>Back to the selected site's homepage. Shared by the reload button and the
        /// remote video-stop path, exactly as on WPF.</summary>
        private void NavigateBrowserToCurrentSiteHome()
        {
            RevealDashboardBrowser("reload");   // WPF Browser.cs:3153 / :603 (site-toggle)
            NavigateBrowser(SiteHomeUrl());
        }

        /// <summary>The site radios' one entry point. Click, not IsCheckedChanged, for the WPF
        /// reason (#867) and an Avalonia one: clicking the site you are already on must still take
        /// you home, and a programmatic IsChecked write raises IsCheckedChanged here, so a change
        /// handler would navigate behind the user's back on every sync.</summary>
        internal void BrowserSiteToggle_Click(object? sender, RoutedEventArgs e)
        {
            if (BrowserBlockedOffline()) return;
            NavigateBrowserToCurrentSiteHome();
        }

        /// <summary>Toolbar reload: back onto the selected site's homepage. The way out of a stuck
        /// page, since re-clicking an already-checked radio raises no change event.</summary>
        internal void BtnReloadBrowser_Click(object? sender, RoutedEventArgs e)
        {
            if (BrowserBlockedOffline()) return;
            NavigateBrowserToCurrentSiteHome();
        }

        /// <summary>Navigates the embedded browser and brings its tab forward. Public because
        /// the callers live elsewhere on WPF (speech-bubble links, the remote controller).</summary>
        /// <param name="autoPlayFullscreen">Accepted for caller compatibility; takeover is still missing.
        /// The browser media/takeover host is not ported. The plain
        /// navigation still happens, which is what every caller needs first.</param>
        public bool NavigateToUrlInBrowser(string url, bool autoPlayFullscreen = false, bool userInitiated = true)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (BrowserBlockedOffline(userInitiated)) return false;

            // WPF MainWindow/MainWindow.Browser.cs:331-385, FocusBrowserSurface, minus the pop-out branch: there is no browser pop-out
            // window on this head, so the embedded surface is always the one to bring forward.
            if (!IsVisible) ShowFromTray();
            else if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            ShowTab("settings");
            RevealDashboardBrowser("focus-surface");   // WPF Browser.cs:349
            Activate();
            Focus();

            // Move the radios to match the URL, deselecting both for an external link so that
            // clicking either one afterwards navigates back to it. Safe to write: the handlers
            // above are Click-driven, so nothing here starts a second navigation.
            var lower = url.ToLowerInvariant();
            var bambi = BrowserPage?.FindControl<RadioButton>("RbBambiCloud");
            var hypno = BrowserPage?.FindControl<RadioButton>("RbHypnoTube");
            if (bambi != null) bambi.IsChecked = lower.Contains("bambicloud.com");
            if (hypno != null) hypno.IsChecked = lower.Contains("hypnotube.com");

            if (autoPlayFullscreen)
                Log.Warning("Auto-play/fullscreen takeover is not ported; navigating without takeover");

            // WPF MainWindow/MainWindow.Browser.cs:487-498: HTTPS only, after the offline gate.
            // ponytail: an unavailable adapter is known immediately; no 12-second ready poll is needed.
            if (BrowserView is { HasEngine: false })
            {
                var navigated = NavigateBrowser(url); // retain the requested URL on the fallback panel
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
                    Platform.ExternalOpener.Open(url);
                return navigated;
            }
            return NavigateBrowser(url);
        }

        // ============================== the profile viewer ==============================

        /// <summary>Clears the search box and puts the "search for a user" plate back up.</summary>
        internal void BtnClearProfile_Click(object? sender, RoutedEventArgs e)
        {
            var search = ProfilePage?.FindControl<TextBox>("TxtProfileSearch");
            if (search != null) search.Text = "";
            ClearProfileViewer();
        }

        /// <summary>Takes whoever's card is up off the screen. Every line of the WPF body is
        /// presentation over controls this head carries. WPF also stops the OgBorderAnimation
        /// storyboard; here collapsing the container re-gates the loop (MainShellWindow.ProfileFx.cs
        /// listens to its IsVisible). Ends in
        /// SetProfileViewingSelf(true): nothing on screen belongs to anyone else any more, so the
        /// "back to me" chip retires and Customize and Privacy come back.</summary>
        internal void ClearProfileViewer()
        {
            try
            {
                var page = ProfilePage;
                if (page == null) return;

                var wrapper = page.FindControl<Grid>("ProfileCardWrapper");
                if (wrapper != null) wrapper.IsVisible = false;
                var empty = page.FindControl<Border>("NoProfileSelected");
                if (empty != null) empty.IsVisible = true;
                var grid = page.FindControl<ItemsControl>("ProfileAchievementGrid");
                if (grid != null) grid.ItemsSource = null;
                var ogBorder = page.FindControl<Border>("OgBorderContainer");
                if (ogBorder != null) ogBorder.IsVisible = false;
                var ogBadge = page.FindControl<Border>("OgBannerBadge");
                if (ogBadge != null) ogBadge.IsVisible = false;
                var patreon = page.FindControl<Image>("ProfilePatreonTierBadge");
                if (patreon != null) patreon.IsVisible = false;

                SetProfileViewingSelf(true);
            }
            catch (Exception ex) { Log.Debug("ClearProfileViewer: {E}", ex.Message); }
        }

        /// <summary>WPF MainWindow.Browser.cs:1677 BtnChangeDisplayName_Click; the request goes through CoreAccount.</summary>
        internal async System.Threading.Tasks.Task ChangeDisplayNameAsync()
        {
            var button = ProfilePage?.FindControl<Button>("BtnChangeDisplayName");
            try
            {
                var s = CoreSettings.Current;
                var currentName = s.UserDisplayName ?? "";
                var dialog = new Dialogs.DisplayNameDialog(isChangeName: true, currentName: currentName);
                if (!await dialog.ShowDialog<bool>(this)) return;

                var newName = dialog.DisplayName;
                if (string.Equals(newName, currentName, StringComparison.Ordinal)) return;

                if (button != null) button.IsEnabled = false;
                var (success, error, resultName) = await CoreAccount.ChangeDisplayNameAsync(newName);
                if (success && resultName != null)
                {
                    s.UserDisplayName = resultName;
                    CoreSettings.Save();
                    var name = ProfilePage?.FindControl<TextBlock>("TxtProfileViewerName");
                    if (name != null) name.Text = resultName;
                    UpdateQuickLoginUI();
                }
                else
                    await MessageDialog.ShowAsync(this, Loc.Get("title_name_change_failed"), error ?? Loc.Get("msg_failed_to_change_display_name"));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error changing display name");
                await MessageDialog.ShowAsync(this, Loc.Get("label_error"), Loc.Get("msg_error_changing_name"));
            }
            finally
            {
                if (button != null) button.IsEnabled = true;
            }
        }

        /// <summary>WPF MainWindow.Browser.cs:1730 BtnDeleteProfile_Click: type DELETE, delete server-side, then a
        /// full sign-out that pushes nothing and wipes the quest file (AccountSeed.Logout(accountDeleted: true)).</summary>
        internal async System.Threading.Tasks.Task DeleteProfileAsync()
        {
            var button = ProfilePage?.FindControl<Button>("BtnDeleteProfile");
            try
            {
                if (!await new Dialogs.DisplayNameDialog("delete").ShowDialog<bool>(this)) return;

                if (button != null) button.IsEnabled = false;
                var (success, error) = await CoreAccount.DeleteAccountAsync();
                if (success)
                {
                    await Platform.AccountSeed.Logout(accountDeleted: true);
                    UpdateQuickLoginUI(accountChanged: true);
                    ClearProfileViewer();
                    await MessageDialog.ShowAsync(this, Loc.Get("title_profile_deleted"), Loc.Get("msg_profile_deleted"));
                }
                else
                    await MessageDialog.ShowAsync(this, Loc.Get("title_deletion_failed"), error ?? Loc.Get("msg_failed_to_delete_profile"));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error deleting profile");
                await MessageDialog.ShowAsync(this, Loc.Get("label_error"), Loc.Get("msg_error_deleting_profile"));
            }
            finally
            {
                if (button != null) button.IsEnabled = true;
            }
        }
    }
}
