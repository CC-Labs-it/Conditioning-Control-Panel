// PORTED-IN-PART from ConditioningControlPanel/MainWindow/MainWindow.Browser.cs (3,084 lines).
//
// LIVE here (the Profile entry BtnDiscordTab_Click left with the rail entries, cd426fe36):
//   * the site navigation, the offline gate and the profile-viewer clear. Restored in full and
//     each is complete, but NOTHING CALLS THEM YET - see "Callers still missing" below.
//
// WHY NAVIGATION IS PORTABLE AT ALL. The blanket "this whole file is WebView2" header that used
// to sit here was wrong. SettingsTabView.axaml already carries <controls:WebHost x:Name=
// "BrowserWebHost"/>, which wraps Avalonia's NativeWebView and draws a legible fallback panel
// naming the page where no engine is installed. Setting its Source IS the navigation, and its
// NavigationCompleted carries the live URL (OnBrowserNavigationCompleted). What WebHost does NOT
// has a post-load script channel; document-created injection and native fullscreen signals remain absent.
//
// The avatar-link path requests autoplay through WebHost's script channel after navigation.
// Native fullscreen may still be refused by the browser's user-activation policy.
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
//     remaining media reporting, duration limits, audio sync and native fullscreen rig, including
//     the four HandleAudioSync* members, HookHapticAudioSyncRearm,
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
using Avalonia.Threading;
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
                var issued = host.Navigate(new Uri(url));   // equal URLs still reload
                if (issued) { if (_avatarBrowserPlaying) EndAvatarBrowserSession(); Log.Information("Browser navigated to {Url}", url); }
                return issued;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Browser navigation failed for {Url}", url);
                return false;
            }
        }

        /// <summary>WPF's <c>_browser.NavigationCompleted</c> handler (MainWindow.Browser.cs:132-160),
        /// minus the audio-sync injection (no document script channel on WebHost). Forwarded by
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
            ++_browserAutoplayGeneration;
            _pendingBrowserAutoplay = null;
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
        /// <param name="autoPlayFullscreen">Request playback and fullscreen after the page loads.</param>
        public bool NavigateToUrlInBrowser(string url, bool autoPlayFullscreen = false, bool userInitiated = true)
        {
            ++_browserAutoplayGeneration;
            _pendingBrowserAutoplay = null;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var target)) return false;
            if (BrowserBlockedOffline(userInitiated)) return false;

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

            var host = BrowserView;
            _pendingBrowserAutoplay = autoPlayFullscreen && host != null ? target : null;
            _browserActiveNavigation = target;
            _browserNavigationObserved = false;
            if (host != null) HookAvatarBrowser(host);
            _browserNavigationStarted = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var issued = NavigateBrowser(url);
            if (!issued) _pendingBrowserAutoplay = null;
            else if (_pendingBrowserAutoplay != null && !IsBrowserPlaylist(target))
                _ = WaitForBrowserDomAsync(host!, target, _browserAutoplayGeneration);
            return issued;
        }

        // WPF Speech.cs:1512-1529 and BrowserMediaService.cs:196-223,333-376.
        // The queue service is not on this head; the live claim still gates its available interactions.
        private WebHost? _hookedAvatarBrowser;
        private Uri? _pendingBrowserAutoplay;
        private Uri? _browserActiveNavigation;
        private bool _browserNavigationObserved;
        private long _browserNavigationStarted;
        private int _browserAutoplayGeneration;
        private volatile bool _avatarBrowserPlaying, _avatarBrowserSessionActive, _avatarBrowserTakeover;
        private bool _avatarBrowserPaused;
        private DateTime _avatarBrowserHeartbeat;
        private DispatcherTimer? _avatarBrowserWatchdog, _avatarBrowserHardStop;
        internal bool AvatarBrowserSessionActive { get => _avatarBrowserSessionActive; private set => _avatarBrowserSessionActive = value; }
        internal bool AvatarBrowserTakeover { get => _avatarBrowserTakeover; private set => _avatarBrowserTakeover = value; }
        internal bool AvatarBrowserBusy => AvatarBrowserTakeover || _avatarBrowserPlaying;
        internal Func<Task> BrowserAutoplayDelay = () => Task.Delay(1500);

        internal void ClaimAvatarBrowserSession()
        {
            ++_browserAutoplayGeneration;
            _pendingBrowserAutoplay = null;
            AvatarBrowserSessionActive = AvatarBrowserTakeover = true;
            _avatarBrowserPaused = _avatarBrowserPlaying = false; _avatarBrowserHardStop?.Stop(); _avatarBrowserHardStop = null;
            _avatarBrowserHeartbeat = DateTime.UtcNow;
            _avatarBrowserWatchdog ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _avatarBrowserWatchdog.Tick -= BrowserWatchdogTick;
            _avatarBrowserWatchdog.Tick += BrowserWatchdogTick;
            _avatarBrowserWatchdog.Start();
        }

        internal void EndAvatarBrowserSession()
        {
            ++_browserAutoplayGeneration;
            _pendingBrowserAutoplay = null;
            AvatarBrowserSessionActive = AvatarBrowserTakeover = _avatarBrowserPaused = _avatarBrowserPlaying = false;
            _avatarBrowserWatchdog?.Stop(); _avatarBrowserHardStop?.Stop(); _avatarBrowserHardStop = null;
        }

        private void BrowserWatchdogTick(object? sender, EventArgs e) => CheckBrowserHeartbeat(DateTime.UtcNow);

        internal void CheckBrowserHeartbeat(DateTime now)
        {
            if (AvatarBrowserSessionActive && (now - _avatarBrowserHeartbeat).TotalSeconds >= (_avatarBrowserPlaying ? 20 : 60))
            {
                if (AvatarBrowserTakeover) StopAvatarBrowserSession();
                else EndAvatarBrowserSession();
            }
        }

        // WPF MainWindow.xaml.cs:2045 and Browser.cs:973: invalidate delayed work, pause, and exit fullscreen.
        internal void StopAvatarBrowserSession()
        {
            ++_browserAutoplayGeneration;
            _pendingBrowserAutoplay = null;
            if (BrowserView is { HasEngine: true } host)
                _ = host.InvokeScriptAsync("window._ccpTakeoverStopped=true;document.querySelectorAll('video,audio').forEach(v=>{try{v.pause()}catch(e){}});if(document.fullscreenElement&&document.exitFullscreen)document.exitFullscreen().catch(()=>{});");
            EndAvatarBrowserSession();
        }

        private void HookAvatarBrowser(WebHost host)
        {
            if (ReferenceEquals(_hookedAvatarBrowser, host)) return;
            _hookedAvatarBrowser = host;
            host.NavigationStarting += OnAvatarBrowserNavigationStarting;
            host.NavigationFinished += OnAvatarBrowserNavigationFinished;
            host.WebMessage += OnAvatarBrowserMessage;
            Closed += (_, _) => StopAvatarBrowserSession();
        }

        // Starts for the armed request establish its chain; later starts follow redirects.
        private void OnAvatarBrowserNavigationStarting(Uri url)
        {
            if (_pendingBrowserAutoplay == null) return;
            if (!_browserNavigationObserved && !url.Equals(_pendingBrowserAutoplay)) return;
            _browserNavigationObserved = true;
            _browserActiveNavigation = url;
        }

        // A document started after the request belongs to the pending navigation generation.
        private const string BrowserDocumentStateScript = "JSON.stringify({url:location.href,state:document.readyState,started:performance.timeOrigin,domEnd:performance.getEntriesByType('navigation')[0]?.domContentLoadedEventEnd ?? performance.timing?.domContentLoadedEventEnd ?? null})";
        internal Func<Task> BrowserDomPollDelay = () => Task.Delay(100);

        private bool IsActiveBrowserDocument(string? result, Uri? expectedUrl = null, bool requireParsed = true)
        {
            if (result == null || _browserActiveNavigation == null) return false;
            try
            {
                using var outer = System.Text.Json.JsonDocument.Parse(result);
                var json = outer.RootElement.ValueKind == System.Text.Json.JsonValueKind.String ? outer.RootElement.GetString() : result;
                using var doc = System.Text.Json.JsonDocument.Parse(json!);
                var root = doc.RootElement;
                if (!Uri.TryCreate(root.GetProperty("url").GetString(), UriKind.Absolute, out var current) ||
                    (expectedUrl != null && !expectedUrl.Equals(current)) ||
                    root.GetProperty("started").GetDouble() <= _browserNavigationStarted ||
                    (requireParsed && root.GetProperty("state").GetString() != "complete" &&
                     (!root.TryGetProperty("domEnd", out var domEnd) || !domEnd.TryGetDouble(out var end) || end <= 0))) return false;
                return true;
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or System.Collections.Generic.KeyNotFoundException) { return false; }
        }

        private async Task WaitForBrowserDomAsync(WebHost host, Uri target, int generation)
        {
            while (generation == _browserAutoplayGeneration && target.Equals(_pendingBrowserAutoplay))
            {
                await BrowserDomPollDelay();
                if (generation != _browserAutoplayGeneration || !target.Equals(_pendingBrowserAutoplay)) return;
                var result = await host.InvokeScriptAsync(BrowserDocumentStateScript);
                if (generation != _browserAutoplayGeneration || !target.Equals(_pendingBrowserAutoplay)) return;
                if (!IsActiveBrowserDocument(result)) continue;
                _pendingBrowserAutoplay = null;
                _ = StartBrowserAutoplay(host, target, generation);
                return;
            }
        }

        private void OnAvatarBrowserNavigationFinished(Uri url, bool success)
        {
            var target = _pendingBrowserAutoplay;
            if (target == null) return;
            // GTK reports completion at commit, which can precede deferred scripts and DOMContentLoaded.
            if (success && !IsBrowserPlaylist(target))
            {
                _ = CompleteVideoNavigationAsync(url, target, _browserAutoplayGeneration);
                return;
            }
            if (!_browserActiveNavigation!.Equals(url))
            {
                if (success) _ = CompleteChangedBrowserUrlAsync(url, target, _browserAutoplayGeneration);
                return;
            }
            _pendingBrowserAutoplay = null;
            if (!success) { EndAvatarBrowserSession(); return; }
            _ = StartBrowserAutoplay(_hookedAvatarBrowser!, target, _browserAutoplayGeneration);
        }

        // If timing is available, an early native completion must leave the DOM poll armed.
        // If the timing entry is absent, keep the completion fallback; a failed script is retried by the poll.
        private async Task CompleteVideoNavigationAsync(Uri url, Uri target, int generation)
        {
            var host = _hookedAvatarBrowser!;
            var result = await host.InvokeScriptAsync(BrowserDocumentStateScript);
            if (generation != _browserAutoplayGeneration || !target.Equals(_pendingBrowserAutoplay)) return;
            if (result == null || DomContentStillPending(result)) return;
            if (!_browserActiveNavigation!.Equals(url) && !IsActiveBrowserDocument(result, url, requireParsed: false)) return;
            _pendingBrowserAutoplay = null;
            _ = StartBrowserAutoplay(host, target, generation);
        }

        private static bool DomContentStillPending(string result)
        {
            try
            {
                using var outer = System.Text.Json.JsonDocument.Parse(result);
                var json = outer.RootElement.ValueKind == System.Text.Json.JsonValueKind.String ? outer.RootElement.GetString() : result;
                using var doc = System.Text.Json.JsonDocument.Parse(json!);
                if (doc.RootElement.TryGetProperty("state", out var state) && state.GetString() == "complete") return false;
                return doc.RootElement.TryGetProperty("domEnd", out var end) &&
                    end.ValueKind != System.Text.Json.JsonValueKind.Null &&
                    (!end.TryGetDouble(out var value) || value <= 0);
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException) { return false; }
        }

        private async Task CompleteChangedBrowserUrlAsync(Uri url, Uri target, int generation)
        {
            var host = _hookedAvatarBrowser!;
            var result = await host.InvokeScriptAsync(BrowserDocumentStateScript);
            if (generation != _browserAutoplayGeneration || !target.Equals(_pendingBrowserAutoplay) ||
                !IsActiveBrowserDocument(result, url, requireParsed: false)) return;
            _pendingBrowserAutoplay = null;
            _ = StartBrowserAutoplay(host, target, generation);
        }

        internal async Task StartBrowserAutoplay(WebHost host, Uri target, int generation)
        {
            await BrowserAutoplayDelay(); // WPF Browser.cs:835-837,1023-1026
            if (generation != _browserAutoplayGeneration || !host.HasEngine) return;
            await host.InvokeScriptAsync((IsBrowserPlaylist(target) ? BrowserPlaylistScript : BrowserVideoScript)
                .Replace("__GEN__", generation.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        internal static bool IsBrowserPlaylist(Uri target) =>
            target.Host.EndsWith("bambicloud.com", StringComparison.OrdinalIgnoreCase) &&
            target.AbsolutePath.Contains("/playlist/", StringComparison.OrdinalIgnoreCase);

        private void OnAvatarBrowserMessage(string message)
        {
            if (!AvatarBrowserSessionActive && !_avatarBrowserPaused) return;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(message);
                var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var type) || type.GetString() != "ccpAvatarMedia" ||
                    !root.TryGetProperty("generation", out var gen) || !gen.TryGetInt32(out var value) || value != _browserAutoplayGeneration ||
                    !root.TryGetProperty("state", out var state)) return;
                switch (state.GetString())
                {
                    case "playing":
                        if (_avatarBrowserPaused) { _avatarBrowserPaused = false; AvatarBrowserSessionActive = true; _avatarBrowserWatchdog?.Start(); }
                        _avatarBrowserPlaying = true; _avatarBrowserHeartbeat = DateTime.UtcNow; break;
                    case "paused":
                        if (!_avatarBrowserPlaying) break;
                        AvatarBrowserSessionActive = AvatarBrowserTakeover = _avatarBrowserPlaying = false; _avatarBrowserPaused = true;
                        _avatarBrowserWatchdog?.Stop(); _avatarBrowserHardStop?.Stop(); _avatarBrowserHardStop = null; break;
                    case "progress": if (_avatarBrowserPlaying) _avatarBrowserHeartbeat = DateTime.UtcNow; break;
                    case "navigated" when !_avatarBrowserPlaying && !_avatarBrowserPaused: break;
                    case "navigated": case "ended": EndAvatarBrowserSession(); break;
                    case "fsExit": AvatarBrowserTakeover = false; break;
                }
                if (_avatarBrowserPlaying && AvatarBrowserTakeover && _avatarBrowserHardStop == null && // WPF BrowserMediaService.cs:230-273,455-490
                    root.TryGetProperty("duration", out var duration) && duration.ValueKind == System.Text.Json.JsonValueKind.Number &&
                    duration.TryGetDouble(out var seconds) && double.IsFinite(seconds) && seconds > 0)
                {
                    var timer = _avatarBrowserHardStop = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Min(seconds + 15, 3600)) };
                    timer.Tick += (_, _) => { timer.Stop(); if (value == _browserAutoplayGeneration && AvatarBrowserTakeover) StopAvatarBrowserSession(); };
                    timer.Start();
                }
            }
            catch (System.Text.Json.JsonException) { }
            catch (InvalidOperationException) { }
        }

        // Adapted from WPF Browser.cs:839-954 and :1028-1075. The carrier is WebView2 on Windows
        // and invokeCSharpAction on WebKit; heartbeat keeps a live session distinct from a stalled load.
        private const string BrowserVideoScript = """
            (async function(){const gen=__GEN__; const send=s=>{const m=JSON.stringify({type:'ccpAvatarMedia',generation:gen,state:s,duration:v?.duration});if(window.chrome?.webview?.postMessage)window.chrome.webview.postMessage(m);else if(window.invokeCSharpAction)window.invokeCSharpAction(m);};
            window._ccpTakeoverStopped=false;let v=document.querySelector('video');window.addEventListener('pagehide',()=>send('navigated'),{once:true});
            for(let i=0;i<20&&!v;i++){await new Promise(r=>setTimeout(r,500));if(window._ccpTakeoverStopped)return;v=document.querySelector('video');}
            if(!v){send('fsExit');return;}let timer;const beat=()=>{if(!v.paused&&!v.ended)send('progress');};
            let pauseTimer;v.addEventListener('playing',()=>{clearTimeout(pauseTimer);send('playing');clearInterval(timer);timer=setInterval(beat,1000);});
            v.addEventListener('pause',()=>{clearTimeout(pauseTimer);pauseTimer=setTimeout(()=>{if(v.paused&&!v.ended)send('paused');},2000);});
            v.addEventListener('ended',()=>{clearInterval(timer);if(document.fullscreenElement&&document.exitFullscreen)document.exitFullscreen().catch(()=>{});send('ended');},{once:true});
            let entered=false;document.addEventListener('fullscreenchange',()=>{if(document.fullscreenElement||document.webkitFullscreenElement)entered=true;else if(entered)send('fsExit');});
            const fs=()=>{if(window._ccpTakeoverStopped)return;try{const p=v.requestFullscreen?v.requestFullscreen():v.webkitRequestFullscreen?.();if(p?.catch)p.catch(()=>{});}catch(e){}};
            v.muted=false;try{await v.play();fs();}catch(e){v.addEventListener('playing',fs,{once:true});}
            })();
            """;
        private const string BrowserPlaylistScript = """
            (async function(){const gen=__GEN__;const send=(s,a)=>{const m=JSON.stringify({type:'ccpAvatarMedia',generation:gen,state:s,duration:a?.duration});if(window.chrome?.webview?.postMessage)window.chrome.webview.postMessage(m);else if(window.invokeCSharpAction)window.invokeCSharpAction(m);};
            window._ccpTakeoverStopped=false;let b=document.querySelector('button.play-action');window.addEventListener('pagehide',()=>send('navigated'),{once:true});
            for(let i=0;i<20&&!b;i++){await new Promise(r=>setTimeout(r,250));if(window._ccpTakeoverStopped)return;b=document.querySelector('button.play-action');}if(!b){send('ended');return;}
            let active;const bind=a=>{if(a.__ccpAvatarBound)return;a.__ccpAvatarBound=true;let timer,pauseTimer;a.addEventListener('pause',()=>{clearTimeout(pauseTimer);pauseTimer=setTimeout(()=>{if(active===a&&a.paused&&!a.ended)send('paused',a);},2000);});a.addEventListener('playing',()=>{clearTimeout(pauseTimer);active=a;send('playing',a);clearInterval(timer);timer=setInterval(()=>{if(!a.paused&&!a.ended)send('progress',a);},1000);});a.addEventListener('ended',()=>{clearInterval(timer);send('ended');});};
            const scan=()=>document.querySelectorAll('audio').forEach(bind);scan();new MutationObserver(scan).observe(document.body,{childList:true,subtree:true});b.click();setTimeout(()=>{if(!window._ccpTakeoverStopped&&!document.querySelector('audio'))send('playing');},3000);
            })();
            """;

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
