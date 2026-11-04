// PORTED-IN-PART from ConditioningControlPanel/MainWindow/MainWindow.Browser.cs (3,084 lines).
//
// LIVE here (the Profile entry BtnDiscordTab_Click left with the rail entries, cd426fe36):
//   * the site navigation, the offline gate and the profile-viewer clear. Restored in full and
//     each is complete, but NOTHING CALLS THEM YET - see "Callers still missing" below.
//
// WebHost supplies a script channel and navigation completion. Video autoplay uses that
// completion as its earliest available signal; WPF also has an earlier DOM-ready signal.
// The general media reporter, shared queue ownership and native fullscreen rig are missing.
// The offline-block toast uses App.Notifications (WPF MainWindow.Browser.cs:489-494).
//
// THE MUTE PAIR is ported onto the card itself (Views/Tabs/SettingsTabView.BrowserCard.cs) over the WebHost
// script channel (Views/Controls/WebHostMedia.cs), re-applied after each navigation. There is still no ENGINE
// mute: a player inside a cross-origin iframe is out of reach.
//
// CALLERS STILL MISSING, each one line in a file this layer does not own:
//   * (Resolved) SettingsTabView forwards RbBambiCloud / RbHypnoTube / BtnReloadBrowser and the
//     WebHost's NavigationCompleted to this partial.
//   * CCP.Avalonia/Views/Tabs/DiscordTabView.axaml.cs:BtnClearProfile_Click now forwards
//     (`Host?.BtnClearProfile_Click`), which wires
//     ClearProfileViewer -> SetProfileViewingSelf. NavigateToUrlInBrowser stays public for
//     AvatarTubeWindow's speech-bubble links and the remote controller, neither on this head.
// STILL BLOCKED, grouped by what is actually missing (60 members):
//   * WebView2 itself (36): the init/teardown chain (InitializeBrowserAsync, InitAndNavigateAsync,
//     TearDownBrowserForReinit, NavigateWhenBrowserReadyAsync, BrowserLoadingText_Click,
//     OpenUrlExternallyAfterBrowserFailure, _browserInitializing, _browserCorePending); the
//     media integration (playlist lifecycle, EndWebVideoTakeover, OnBrowserWebMessageReceived,
//     HandleBrowserMediaMessage, the four HandleAudioSync* members, HookHapticAudioSyncRearm,
//     OnHapticConnectionChangedForAudioSync, _hapticAudioSyncConnHooked); the pop-out and
//     fullscreen rig (BtnPopOutBrowser_Click, FocusBrowserSurface's pop-out half,
//     HandleBrowserFullscreenChanged, Arm/DisarmFullscreenEscapes, Enter/ExitBrowserFullscreen,
//     ExitBrowserFullscreenForTeardown, the four _browserFs* fields); the remote video pair
//     (PlayHypnotubeFromRemote, StopBrowserVideoFromRemote, _remoteBrowserVideoActive); the
//     refused mute pair above.
//   * the Trainer Card read (search, own card, lookup, badges, achievements, FormatNumber) is
//     PORTED onto CCP.Avalonia/Views/Tabs/DiscordTabView.axaml.cs, text rules in Core
//     TrainerCardText. Still missing: BtnDiscordTabLogin_Click, UpdateDiscordTabUI,
//     ResolveProfilePictureUnavailable, LoadPatreonBadgeImage, ProfileDiscordHandle_Click,
//     BtnProfileDiscord_Click (art). Change name / delete profile are ported below.
//   * CoreMods (1): SyncSiteRadiosToActiveMod needs ShowBambiCloudOption() and
//     GetDefaultBrowserUrl(); CCP.Core/CoreMods.cs carries neither yet. Its IsBrowserShowingKnownSite
//     helper is NOT restored either: WebHost.CurrentUrl now answers it, but its only caller is
//     SyncSiteRadiosToActiveMod, so write it with that caller.

using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
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

        private int _browserRequestGeneration;
        private Action<Uri, bool>? _pendingVideoAutoPlay;
        private WebHost? _pendingVideoHost;
        private WebHost? _playlistHost;
        private string? _playlistToken;
        private bool _playlistClaimed, _playlistLoaded, _playlistPassive, _playlistScriptInjected;
        private volatile bool _playlistPlaying, _playlistSlotHeld;
        private DateTime _playlistHeartbeat;
        private DispatcherTimer? _playlistWatchdog;
        internal Func<DateTime> PlaylistClock = () => DateTime.UtcNow;

        // WPF BrowserMediaService.cs:196-223 claims before navigation, best effort if busy.
        // This head has no shared interaction queue; the slot here protects Takeover only.
        internal bool PlaylistBlocksAutonomy => _playlistSlotHeld ||
            (_playlistPlaying && CoreSettings.Current.ProtectBrowserVideoPlayback);

        private void ClaimPlaylist(WebHost host)
        {
            _playlistHost = host;
            _playlistToken = Guid.NewGuid().ToString("N");
            _playlistLoaded = _playlistPassive = _playlistScriptInjected = false;
            _playlistClaimed = true;
            _playlistPlaying = false;
            _playlistSlotHeld = !Autonomy.IsBusy();
            _playlistHeartbeat = PlaylistClock();
            host.WebMessage += OnPlaylistMessage;
            host.NavigationFinished += OnPlaylistNavigation;
            _playlistWatchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _playlistWatchdog.Tick += OnPlaylistWatchdog;
            _playlistWatchdog.Start();
        }

        private void ReleasePlaylist()
        {
            if (_playlistHost is { } host)
            {
                host.WebMessage -= OnPlaylistMessage;
                host.NavigationFinished -= OnPlaylistNavigation;
                _ = host.InvokeScriptAsync("window.__ccpPlaylistCleanup?.();");
            }
            _playlistHost = null;
            _playlistToken = null;
            _playlistClaimed = _playlistPlaying = _playlistSlotHeld = false;
            if (_playlistWatchdog != null)
            {
                _playlistWatchdog.Stop();
                _playlistWatchdog.Tick -= OnPlaylistWatchdog;
                _playlistWatchdog = null;
            }
        }

        private void OnPlaylistNavigation(Uri url, bool success)
        {
            if (!_playlistLoaded && success)
            {
                _playlistLoaded = true;
                if (_playlistPassive && !_playlistScriptInjected) InstallPassivePlaylist();
                return;
            }
            CancelVideoAutoPlay(); // failed first load or a later navigation, including an in-page link
        }

        private void OnPlaylistMessage(string raw)
        {
            var prefix = "ccp-playlist:" + _playlistToken + ":";
            var message = raw.Trim().Trim('"');
            if (_playlistToken == null || !message.StartsWith(prefix, StringComparison.Ordinal)) return;
            switch (message[prefix.Length..])
            {
                case "playing":
                    if (!_playlistClaimed)
                    {
                        _playlistClaimed = true;
                        _playlistSlotHeld = !Autonomy.IsBusy();
                        _playlistWatchdog?.Start();
                    }
                    _playlistPlaying = true;
                    _playlistHeartbeat = PlaylistClock();
                    break;
                case "progress":
                    if (_playlistPlaying) _playlistHeartbeat = PlaylistClock();
                    break;
                case "ended":
                    _playlistClaimed = _playlistPlaying = _playlistSlotHeld = false;
                    _playlistWatchdog?.Stop();
                    break;
                case "failed":
                case "navigated":
                    CancelVideoAutoPlay();
                    break;
            }
        }

        // WPF BrowserMediaService.cs:70-78,523-543: 60s to load, then 20s without a beat.
        private void OnPlaylistWatchdog(object? sender, EventArgs e) => CheckPlaylistTimeout();
        internal void CheckPlaylistTimeout()
        {
            if (_playlistClaimed && (PlaylistClock() - _playlistHeartbeat).TotalSeconds >= (_playlistPlaying ? 20 : 60))
                StopBrowserMediaForPanic();
        }

        // WPF MainWindow.Browser.cs:793-815 arms once before navigation, and :835-837
        // waits 1.5 seconds before injection. WebHost has completion but no DOM-ready event.
        internal Func<Task> BrowserAutoPlayDelay = () => Task.Delay(1500);

        private void InstallPassivePlaylist()
        {
            if (_playlistHost == null || _playlistToken == null) return;
            _playlistScriptInjected = true;
            _ = _playlistHost.InvokeScriptAsync(PlaylistAutoPlayScript.Replace("__CCP_PLAYLIST_TOKEN__", _playlistToken)
                .Replace("__CCP_PLAYLIST_PASSIVE__", "yes"));
        }

        private void CancelVideoAutoPlay(bool keepPlaylist = false)
        {
            ++_browserRequestGeneration;
            if (_pendingVideoHost != null && _pendingVideoAutoPlay != null)
                _pendingVideoHost.NavigationFinished -= _pendingVideoAutoPlay;
            _pendingVideoHost = null;
            _pendingVideoAutoPlay = null;
            if (keepPlaylist && _playlistHost != null)
            {
                _playlistClaimed = _playlistPlaying = _playlistSlotHeld = false;
                (_playlistPassive, _playlistScriptInjected, _playlistToken) = (true, false, Guid.NewGuid().ToString("N"));
                _playlistWatchdog?.Stop();
            }
            else ReleasePlaylist();
        }

        // WPF MainWindow.xaml.cs:2042-2045 and MainWindow.Browser.cs:973-990.
        // Invalidate the delayed injection before asking the current page to stop.
        internal void StopBrowserMediaForPanic()
        {
            CancelVideoAutoPlay(keepPlaylist: true);
            if (BrowserView is { HasEngine: true } host)
                _ = host.InvokeScriptAsync(PanicBrowserMediaScript);
            if (_playlistLoaded && !_playlistScriptInjected) InstallPassivePlaylist();
        }

        internal const string PanicBrowserMediaScript = """
            (function() {
                window._ccpForcedFs = false;
                window._ccpTakeoverStopped = true;
                if (window.__ccpPlaylistCleanup) window.__ccpPlaylistCleanup();
                document.querySelectorAll('video,audio').forEach(v => { try { v.pause(); } catch (e) {} });
                try {
                    const exit = document.exitFullscreen || document.webkitExitFullscreen || document.msExitFullscreen;
                    if (exit && (document.fullscreenElement || document.webkitFullscreenElement || document.msFullscreenElement)) {
                        const result = exit.call(document);
                        if (result && result.catch) result.catch(() => {});
                    }
                } catch (e) {}
            })();
            """;

        private void ArmVideoAutoPlay(WebHost host, bool playlist)
        {
            var request = _browserRequestGeneration;
            void OnFinished(Uri completedUrl, bool success)
            {
                host.NavigationFinished -= OnFinished;
                _pendingVideoHost = null;
                _pendingVideoAutoPlay = null;
                if (success) _ = PlayVideoAfterLoadAsync(host, request, playlist);
            }
            _pendingVideoHost = host;
            _pendingVideoAutoPlay = OnFinished;
            host.NavigationFinished += OnFinished;
        }

        private async Task PlayVideoAfterLoadAsync(WebHost host, int request, bool playlist)
        {
            await BrowserAutoPlayDelay();
            if (request != _browserRequestGeneration) return;
            if (playlist) _playlistScriptInjected = true;
            var script = playlist ? PlaylistAutoPlayScript.Replace("__CCP_PLAYLIST_TOKEN__", _playlistToken!) : VideoAutoPlayScript;
            await host.InvokeScriptAsync(script);
        }

        // WPF MainWindow.Browser.cs:843-955: poll for the player, play, request fullscreen,
        // retry fullscreen after a blocked play, and leave fullscreen on end or double-click.
        // Media reporting and metadata are deferred until the browser media service exists.
        internal const string VideoAutoPlayScript = """
            (async function() {
                window._ccpTakeoverStopped = false;
                let video = document.querySelector('video');
                for (let i = 0; i < 20 && !video; i++) {
                    await new Promise(r => setTimeout(r, 500));
                    if (window._ccpTakeoverStopped) return;
                    video = document.querySelector('video');
                }
                if (!video) return;
                const exitFullscreen = () => {
                    if (document.exitFullscreen) document.exitFullscreen();
                    else if (document.webkitExitFullscreen) document.webkitExitFullscreen();
                    else if (document.msExitFullscreen) document.msExitFullscreen();
                };
                video.addEventListener('ended', exitFullscreen, { once: true });
                video.addEventListener('dblclick', e => {
                    if (document.fullscreenElement || document.webkitFullscreenElement) {
                        exitFullscreen();
                        e.preventDefault();
                        e.stopPropagation();
                    }
                });
                if (!window.__ccpMuted) video.muted = false;
                const goFullscreen = () => {
                    if (window._ccpTakeoverStopped) return;
                    if (video.requestFullscreen) {
                        const request = video.requestFullscreen();
                        if (request && request.catch) request.catch(() => {});
                    } else if (video.webkitRequestFullscreen) video.webkitRequestFullscreen();
                    else if (video.msRequestFullscreen) video.msRequestFullscreen();
                };
                video.play().then(goFullscreen).catch(() => {
                    video.addEventListener('playing', goFullscreen, { once: true });
                });
            })();
            """;

        // WPF MainWindow.Browser.cs:1017-1073 and BrowserService.cs:390-430.
        // A per-navigation token keeps late messages from an outgoing page from releasing a new claim.
        internal const string PlaylistAutoPlayScript = """
            (async function() {
                const token = '__CCP_PLAYLIST_TOKEN__';
                window.__ccpPlaylistToken = token;
                window._ccpTakeoverStopped = false;
                const current = () => window.__ccpPlaylistToken === token && !window._ccpTakeoverStopped;
                const post = state => {
                    if (!current()) return;
                    const message = 'ccp-playlist:' + token + ':' + state;
                    try {
                        if (window.invokeCSharpAction) window.invokeCSharpAction(message);
                        else if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage(message);
                    } catch (_) {}
                };
                let active = null, notified = false, beat = null, fallback = null;
                const observer = new MutationObserver(() => scan());
                const cleanup = () => { observer.disconnect(); clearInterval(beat); clearTimeout(fallback); document.removeEventListener('playing', wake, true); beat = fallback = null; };
                window.__ccpPlaylistCleanup = cleanup;
                const bind = audio => {
                    if (!audio || audio.__ccpPlaylistBound === token) return;
                    audio.__ccpPlaylistBound = token;
                    audio.addEventListener('playing', () => { active = audio; notified = true; post('playing'); });
                    audio.addEventListener('ended', () => { active = null; notified = true; post('ended'); });
                    audio.addEventListener('pause', () => setTimeout(() => {
                        if (active === audio && audio.paused && !audio.ended) { active = null; post('ended'); }
                    }, 2000));
                };
                const scan = () => document.querySelectorAll('audio').forEach(bind);
                const start = () => {
                    scan();
                    observer.observe(document.body, { childList: true, subtree: true });
                    if (beat === null) beat = setInterval(() => { if (active && !active.paused && !active.ended) post('progress'); }, 1000);
                };
                const wake = e => {
                    if (window.__ccpPlaylistToken !== token || !window._ccpTakeoverStopped || e.target?.tagName !== 'AUDIO') return;
                    window._ccpTakeoverStopped = false;
                    active = e.target; notified = true; post('playing'); start();
                };
                document.addEventListener('playing', wake, true);
                window.addEventListener('pagehide', () => { post('navigated'); cleanup(); document.removeEventListener('playing', wake, true); }, { once: true });
                if ('__CCP_PLAYLIST_PASSIVE__' === 'yes') { window._ccpTakeoverStopped = true; return; }
                start();
                let btn = document.querySelector('button.play-action');
                for (let i = 0; i < 20 && !btn; i++) {
                    await new Promise(r => setTimeout(r, 250));
                    if (!current()) return;
                    btn = document.querySelector('button.play-action');
                }
                if (!btn) { cleanup(); post('failed'); return; }
                if (!current()) return;
                try { btn.click(); } catch (_) { cleanup(); post('failed'); return; }
                fallback = setTimeout(() => { if (!notified) { notified = true; post('playing'); } }, 3000);
            })();
            """;

        /// <summary>Points the web view at a URL. A missing host returns false.</summary>
        private bool NavigateBrowser(string url, bool autoPlayFullscreen = false)
        {
            CancelVideoAutoPlay();
            try
            {
                var host = BrowserView;
                if (host == null) return false;
                var target = new Uri(url);
                var playlist = autoPlayFullscreen && url.Contains("bambicloud.com/playlist/", StringComparison.OrdinalIgnoreCase);
                if (playlist && host.HasEngine) ClaimPlaylist(host);
                if (autoPlayFullscreen && host.HasEngine) ArmVideoAutoPlay(host, playlist);
                var refusals = host.RefusedNavigations;
                host.Navigate(target);   // even when equal: WPF #867, re-click goes home
                if (playlist && host.RefusedNavigations != refusals) { CancelVideoAutoPlay(); return false; }
                Log.Information("Browser navigated to {Url}", url);
                return true;
            }
            catch (Exception ex)
            {
                CancelVideoAutoPlay();
                Log.Warning(ex, "Browser navigation failed for {Url}", url);
                return false;
            }
        }

        /// <summary>WPF's <c>_browser.NavigationCompleted</c> handler (MainWindow.Browser.cs:132-160),
        /// minus the audio-sync injection. Forwarded by
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
        /// <param name="autoPlayFullscreen">Requests video playback and fullscreen after navigation.
        /// Playlist playback claims a Takeover guard; the shared browser media service remains unported.</param>
        public bool NavigateToUrlInBrowser(string url, bool autoPlayFullscreen = false, bool userInitiated = true)
        {
            if (string.IsNullOrWhiteSpace(url) || BrowserBlockedOffline(userInitiated))
            {
                CancelVideoAutoPlay();
                return false;
            }

            // WPF's FocusBrowserSurface, minus the pop-out branch: there is no browser pop-out
            // window on this head, so the embedded surface is always the one to bring forward.
            ShowTab("settings");
            RevealDashboardBrowser("focus-surface");   // WPF Browser.cs:349
            Activate();

            // Move the radios to match the URL, deselecting both for an external link so that
            // clicking either one afterwards navigates back to it. Safe to write: the handlers
            // above are Click-driven, so nothing here starts a second navigation.
            var lower = url.ToLowerInvariant();
            var bambi = BrowserPage?.FindControl<RadioButton>("RbBambiCloud");
            var hypno = BrowserPage?.FindControl<RadioButton>("RbHypnoTube");
            if (bambi != null) bambi.IsChecked = lower.Contains("bambicloud.com");
            if (hypno != null) hypno.IsChecked = lower.Contains("hypnotube.com");

            // WPF BrowserService.cs:1205-1214 returns false if no native adapter accepted
            // the request. Naming a URL in WebHost's fallback panel is not navigation:
            // companion watch chips need this failure to reach their HTTPS fallback.
            return NavigateBrowser(url, autoPlayFullscreen) && BrowserView?.HasEngine == true;
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
