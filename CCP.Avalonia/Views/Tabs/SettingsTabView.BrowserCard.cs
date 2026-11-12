// PORTED from WPF 7.1.5, the Home browser card. IMPORTED-AND-ADAPTED from trunk e3109afdf / 376305e62:
//   BtnMuteBrowser_Click + SyncBrowserMuteIcon     MainWindow.Browser.cs:2603, :2613
//   BtnWebcamTracking_Click + RefreshBrowserWebcamButton   MainWindow.DeeperTab.cs:245, :228
//   ChkForceShowBambiCloud_Changed                 MainWindow.DeeperTab.cs:505
//   BrowserLoadingText_Click                       MainWindow.Browser.cs:280
// Mute rides the WebHost script channel (Views/Controls/WebHostMedia.cs) and is re-applied after every navigation,
// since a new document has no flag. The load prompt: WPF builds the WebView2 on that click; here the engine exists
// from the start but loads nothing until asked, so the prompt stands in the slot until the first page.
// Pop out preserves the native adapter with BeginReparenting.
// "Enhance if possible" remains disabled without BrowserEnhanceBridge.

using System;
using Avalonia.Controls;
using Avalonia.Data;
using ConditioningControlPanel.Avalonia.Localization;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class SettingsTabView
    {
        private bool _browserCardSyncing;
        internal static volatile bool RemixBrowserMediaBusy;
        private static volatile bool _remixWatchActive;
        internal static long RemixMediaTick;
        internal static bool RemixBrowserVideoBusy => RemixBrowserMediaBusy || (_remixWatchActive
            && System.Diagnostics.Stopwatch.GetElapsedTime(System.Threading.Interlocked.Read(ref RemixMediaTick)).TotalSeconds > 3);
        private string? _remixMediaId;

        // WPF BrowserService.cs:337: watch user-started media, not only takeover playback.
        // Recognised players report their state. Unknown or stale cross-origin state stays busy.
        internal const string RemixMediaScript = """
            (() => {
              const id = __CCP_TOKEN__;
              if (window.__ccpRemixWatch) clearInterval(window.__ccpRemixWatch);
              if (window.__ccpRemixAbort) window.__ccpRemixAbort.abort();
              const controller = window.__ccpRemixAbort = new AbortController();
              let lastPlaying = 0;
              const states = new WeakMap();
              let players = [];
              const player = f => {
                try {
                  const url = new URL(f.src, document.baseURI), host = url.hostname;
                  const kind = host === 'player.vimeo.com' ? 'vimeo'
                    : (host === 'www.youtube.com' || host === 'www.youtube-nocookie.com') && url.searchParams.get('enablejsapi') === '1' ? 'youtube' : null;
                  if (!kind || !f.contentWindow) return true;
                  let state = states.get(f);
                  if (!state || state.src !== url.href) { state = { src: url.href, active: true, time: 0, subscribed: false, ready: false }; states.set(f, state); }
                  players.push({ f, origin: url.origin, kind, state });
                  const send = o => f.contentWindow.postMessage(JSON.stringify(o), url.origin);
                  if (kind === 'vimeo') send({ method: 'getPaused' });
                  else {
                    if (!state.ready) send({ event: 'listening', id: 'ccp-remix' });
                    if (!state.subscribed) {
                      send({ event: 'command', func: 'addEventListener', args: ['onStateChange'], id: 'ccp-remix' });
                      state.subscribed = true;
                    }
                    send({ event: 'command', func: 'getPlayerState', args: [], id: 'ccp-remix' });
                  }
                  return !state.time || Date.now() - state.time > 3000 || state.active;
                } catch (_) { return true; }
              };
              const busy = d => {
                let active = [...d.querySelectorAll('video,audio')].some(m => !m.paused && !m.ended && (m.tagName === 'AUDIO' ? !m.muted : m.offsetWidth >= 200));
                for (const f of d.querySelectorAll('iframe')) {
                  if (f.offsetWidth < 200) continue;
                  try {
                    if (!f.contentDocument) active = player(f) || active;
                    else active = busy(f.contentDocument) || active;
                  } catch (_) { active = player(f) || active; }
                }
                return active;
              };
              const report = () => {
                let active = true;
                try { players = []; active = busy(document); } catch (_) {}
                if (active) lastPlaying = Date.now();
                else active = lastPlaying > 0 && Date.now() - lastPlaying < 2000;
                const msg = JSON.stringify({ type: 'ccpRemixMedia', id, active });
                if (typeof window.invokeCSharpAction === 'function') window.invokeCSharpAction(msg);
                else if (window.chrome?.webview) window.chrome.webview.postMessage(msg);
              };
              window.addEventListener('message', e => {
                const p = players.find(p => p.f.contentWindow === e.source && p.origin === e.origin);
                if (!p) return;
                let data = e.data;
                try { if (typeof data === 'string') data = JSON.parse(data); } catch (_) { return; }
                if (!data || typeof data !== 'object') return;
                let active;
                if (p.kind === 'vimeo' && data.method === 'getPaused' && typeof data.value === 'boolean') active = !data.value;
                if (p.kind === 'youtube') {
                  if (data.event === 'onReady' && !p.state.ready) { p.state.ready = true; p.state.subscribed = false; }
                  const value = data.event === 'onStateChange' ? data.info
                    : data.event === 'infoDelivery' || data.event === 'initialDelivery' ? data.info?.playerState : null;
                  if (typeof value === 'number') active = ![-1, 0, 2, 5].includes(value);
                }
                if (active === undefined) return;
                p.state.active = active; p.state.time = Date.now();
              }, { signal: controller.signal });
              for (const event of ['playing', 'pause', 'ended', 'volumechange']) document.addEventListener(event, report, { capture: true, signal: controller.signal });
              window.__ccpRemixWatch = setInterval(report, 500);
              report();
            })();
            """;

        private void BeginRemixMediaWatch()
        {
            _remixMediaId = null;
            var hasEngine = BrowserWebHost.HasEngine;
            RemixBrowserMediaBusy = hasEngine;
            System.Threading.Interlocked.Exchange(ref RemixMediaTick, System.Diagnostics.Stopwatch.GetTimestamp());
            _remixWatchActive = hasEngine;
        }

        private void InstallRemixMediaWatch(Uri uri, bool success)
        {
            if (!BrowserWebHost.HasEngine) { _remixWatchActive = false; RemixBrowserMediaBusy = false; return; }
            BeginRemixMediaWatch();
            if (!success) return; // Unknown playback fails closed until the next navigation.
            _remixMediaId = Guid.NewGuid().ToString("N");
            _ = BrowserWebHost.InvokeScriptAsync(RemixMediaScript.Replace("__CCP_TOKEN__", System.Text.Json.JsonSerializer.Serialize(_remixMediaId)));
        }

        private void OnRemixMediaMessage(string body)
        {
            try
            {
                using var outer = System.Text.Json.JsonDocument.Parse(body);
                var payload = outer.RootElement.ValueKind == System.Text.Json.JsonValueKind.String ? outer.RootElement.GetString() : body;
                using var json = System.Text.Json.JsonDocument.Parse(payload!);
                var root = json.RootElement;
                if (_remixMediaId != null && root.GetProperty("type").GetString() == "ccpRemixMedia"
                    && root.GetProperty("id").GetString() == _remixMediaId && root.GetProperty("active").ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False)
                {
                    var active = root.GetProperty("active").GetBoolean();
                    if (active) RemixBrowserMediaBusy = true;
                    System.Threading.Interlocked.Exchange(ref RemixMediaTick, System.Diagnostics.Stopwatch.GetTimestamp());
                    if (!active) RemixBrowserMediaBusy = false;
                }
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or System.Collections.Generic.KeyNotFoundException) { }
        }

        /// <summary>Tests: how many times the mute script was sent to the page.</summary>
        internal int BrowserMuteApplies { get; private set; }

        private void HookBrowserCard()
        {
            BrowserWebHost.NavigationCompleted += _ => ApplyBrowserMute();
            BrowserWebHost.NavigationBeginning += BeginRemixMediaWatch;
            BrowserWebHost.NavigationFinished += InstallRemixMediaWatch;
            BrowserWebHost.WebMessage += OnRemixMediaMessage;
            BrowserWebHost.AttachedToVisualTree += (_, _) =>
            {
                if (BrowserWebHost.CurrentUrl is { } current) InstallRemixMediaWatch(current, true);
                else if (BrowserWebHost.Source != null) BeginRemixMediaWatch();
            };
            BrowserWebHost.DetachedFromVisualTree += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                if (!BrowserWebHost.IsAttachedToVisualTree()) { _remixMediaId = null; _remixWatchActive = false; RemixBrowserMediaBusy = false; }
            });
            BrowserWebHost.NavigationFinished += (url, success) =>
                _ = Browser.BrowserAdCss.ApplyAsync(url, success, BrowserWebHost.InvokeScriptAsync);
            BrowserWebHost.PropertyChanged += (_, e) => { if (e.Property == WebHost.SourceProperty) SyncBrowserPrompt(); };
            AttachedToVisualTree += (_, _) =>
            {
                Platform.WebcamTracker.Instance.StateChanged -= OnBrowserCardTrackerChanged;
                Platform.WebcamTracker.Instance.StateChanged += OnBrowserCardTrackerChanged;
                CoreMods.ModChanged -= OnBrowserCardModChanged;
                CoreMods.ModChanged += OnBrowserCardModChanged;
                SyncBrowserCard();
            };
            DetachedFromVisualTree += (_, _) =>
            {
                Platform.WebcamTracker.Instance.StateChanged -= OnBrowserCardTrackerChanged;
                CoreMods.ModChanged -= OnBrowserCardModChanged;
            };
            foreach (Control c in new Control[] { ToggleEnhanceIfPossible })
            {
                c.IsEnabled = false;
                c.Bind(ToolTip.TipProperty, LocBinding("exclusives_not_on_this_build"));
                ToolTip.SetShowOnDisabled(c, true);
            }
            SyncBrowserCard();
        }

        private void OnBrowserCardTrackerChanged() => Dispatcher.UIThread.Post(RefreshBrowserWebcamButton);
        private void OnBrowserCardModChanged(object? sender, ModPackage? mod) => Dispatcher.UIThread.Post(OnBrowserModSwitched);

        /// <summary>WPF MainWindow/MainWindow.xaml.cs:3244-3262: the radios follow the new mod, and a mod that does not offer
        /// BambiCloud also sends an open page to its own default, even when the override reveals the radio. Only a
        /// page that was ever loaded moves (WPF: _browserInitialized), and never offline (#867).</summary>
        internal void OnBrowserModSwitched()
        {
            SyncBrowserCard();
            if (App.Mods is { } mods && !mods.ShowBambiCloudOption() && BrowserWebHost.Source != null
                && !CoreSettings.Current.OfflineMode)
                BrowserWebHost.Navigate(new Uri(mods.GetDefaultBrowserUrl()));
        }

        /// <summary>The saved state into the row: the mute glyph, the webcam pill, the BambiCloud override and the prompt.</summary>
        internal void SyncBrowserCard()
        {
            _browserCardSyncing = true;
            try { ChkForceShowBambiCloud.IsChecked = CoreSettings.Current.ForceShowBambiCloud; }
            finally { _browserCardSyncing = false; }
            SyncBrowserMuteIcon();
            RefreshBrowserWebcamButton();
            ApplyBambiCloudVisibility(navigateIfHidden: false);
            SyncBrowserPrompt();
        }

        // ------------------------------------------------------------------ mute

        /// <summary>WPF BtnMuteBrowser_Click: flip the saved flag, apply it live, save, repaint the glyph.</summary>
        private void BtnMuteBrowser_Click(object? sender, RoutedEventArgs e)
        {
            var s = CoreSettings.Current;
            s.BrowserVideoMuted = !s.BrowserVideoMuted;
            ApplyBrowserMute();
            CoreSettings.Save();
            SyncBrowserMuteIcon();
        }

        /// <summary>Sends the saved mute state to the live page (WPF: BrowserService re-applies IsAudioMuted on init).</summary>
        internal void ApplyBrowserMute()
        {
            BrowserMuteApplies++;
            // While the Workshop pause holds, a new page stays muted whatever the saved preference.
            _ = BrowserWebHost.SetMutedAsync(BrowserWebHost.MediaPaused || CoreSettings.Current.BrowserVideoMuted);
        }

        /// <summary>WPF SyncBrowserMuteIcon: the header glyph matches the saved preference.</summary>
        internal void SyncBrowserMuteIcon() =>
            TxtBrowserMute.Kind = CoreSettings.Current.BrowserVideoMuted ? IconKind.SpeakerMute : IconKind.Speaker2;

        // ------------------------------------------------------------------ webcam

        /// <summary>WPF BtnWebcamTracking_Click: stop if running; else the consent gate (declined = nothing), the
        /// calibration warning when there is none, a start off the UI thread, then calibration on the live stream
        /// (cancelled = the camera stops again).</summary>
        private async void BtnWebcamTracking_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                try { CoreBark.NotifyUiAction("webcam_tracking"); } catch (Exception ex) { Log.Debug("webcam bark: {E}", ex.Message); }
                var tracker = Platform.WebcamTracker.Instance;
                if (tracker.IsRunning)
                {
                    await tracker.StopAsync();
                    RefreshBrowserWebcamButton();
                    return;
                }
                if (TopLevel.GetTopLevel(this) is not Window owner) return;
                if (!Services.Webcam.WebcamConsent.IsCurrent(CoreSettings.Current))
                {
                    await new Dialogs.WebcamConsentDialog().ShowDialogSafe(owner);
                    if (!Services.Webcam.WebcamConsent.IsCurrent(CoreSettings.Current)) return;
                }
                bool needsCalibration = tracker.Calibration == null;
                if (needsCalibration && !await Dialogs.MessageDialog.ConfirmAsync(owner,
                        Loc.Get("browser_webcam_calibrate_prompt_title"), Loc.Get("browser_webcam_calibrate_prompt_body")))
                    return;

                bool started = await tracker.StartAsync();
                RefreshBrowserWebcamButton();
                if (!started) { Log.Warning("Browser webcam toggle: start failed ({E})", tracker.LastError); return; }
                if (needsCalibration && await Windows.WebcamCalibrationWindow.ShowDialogWithRecalibrate(owner) != true)
                {
                    await tracker.StopAsync();
                    RefreshBrowserWebcamButton();
                }
            }
            catch (Exception ex) { Log.Warning(ex, "Browser webcam button failed"); }
        }

        /// <summary>WPF RefreshBrowserWebcamButton: the pill's words and tooltip follow the tracker.</summary>
        internal void RefreshBrowserWebcamButton()
        {
            bool on = Platform.WebcamTracker.Instance.IsRunning;
            TxtWebcamTracking.Key = on ? "btn_browser_webcam_tracking_on" : "btn_browser_webcam_tracking_off";
            BtnWebcamTracking.Bind(ToolTip.TipProperty, LocBinding(on ? "tooltip_browser_webcam_tracking_on" : "tooltip_browser_webcam_tracking_off"));
        }

        // ------------------------------------------------------------------ BambiCloud override

        /// <summary>WPF ChkForceShowBambiCloud_Changed (#596): reveal the BambiCloud toggle on mods that hide it.</summary>
        private void ChkForceShowBambiCloud_Changed(object? sender, RoutedEventArgs e)
        {
            if (_browserCardSyncing) return;
            CoreSettings.Current.ForceShowBambiCloud = ChkForceShowBambiCloud.IsChecked == true;
            CoreSettings.Save();
            ApplyBambiCloudVisibility(navigateIfHidden: true);
        }

        /// <summary>The radio shows when the mod wants it OR the user forces it. Hidden while selected, the dot moves to
        /// HypnoTube and, on a user change, the page follows (WPF SyncSiteRadiosToActiveMod(navigateIfChanged: true)).</summary>
        private void ApplyBambiCloudVisibility(bool navigateIfHidden)
        {
            var modWants = App.Mods?.ShowBambiCloudOption() ?? true;
            RbBambiCloud.IsVisible = modWants || CoreSettings.Current.ForceShowBambiCloud;
            SyncBrowserSite(modWants, App.Mods?.GetDefaultBrowserUrl() ?? "https://bambicloud.com/", navigateIfHidden);
        }

        // WPF MainWindow/MainWindow.Browser.cs:547-580, SyncSiteRadiosToActiveMod: visibility is not the site's default. In particular,
        // revealing BambiCloud with the override must not change a HypnoTube mod's default.
        internal void SyncBrowserSite(bool modWantsBambiCloud, string defaultUrl, bool navigateIfChanged)
        {
            var current = BrowserWebHost.CurrentUrl?.AbsoluteUri;
            var known = current != null && (current.Contains("bambicloud.com", StringComparison.OrdinalIgnoreCase)
                || current.Contains("hypnotube.com", StringComparison.OrdinalIgnoreCase));
            if (modWantsBambiCloud && known) return;
            var onBambi = modWantsBambiCloud && defaultUrl.Contains("bambicloud.com", StringComparison.OrdinalIgnoreCase);
            var target = onBambi ? RbBambiCloud : RbHypnoTube;
            var moved = target.IsChecked != true;
            target.IsChecked = true;
            if (moved && navigateIfChanged && !CoreSettings.Current.OfflineMode)
                Shell?.BrowserSiteToggle_Click(target, new RoutedEventArgs());
        }

        // ------------------------------------------------------------------ the load prompt

        /// <summary>The prompt stands in the slot while an engine is there and nothing was ever loaded. With no engine
        /// the host's own panel says why, so the prompt stays out of its way.</summary>
        internal static bool BrowserPromptShown(bool hasEngine, bool loaded) => hasEngine && !loaded;

        internal void SyncBrowserPrompt()
        {
            bool popped = Shell?.IsBrowserPoppedOut == true;
            bool offline = CoreSettings.Current.OfflineMode;
            bool prompt = popped || offline || BrowserPromptShown(BrowserWebHost.HasEngine, BrowserWebHost.Source != null);
            BrowserLoadingText.IsVisible = prompt;
            BrowserLoadingText.Bind(TextBlock.TextProperty, (Binding)new StrBareExtension(offline
                ? "label_browser_disabled_in_offline_mode" : popped   // WPF MainWindow.Browser.cs:2737
                ? "label_browser_popped_out_nclick_to_focus_window" : "label_click_to_load_browser").ProvideValue(null!));
            // A native web view draws over anything laid on it, so the prompt only shows with the host hidden.
            // The placeholder belongs to the card; the popped-out host stays visible in its window.
            BrowserWebHost.IsVisible = popped || !prompt;
            // Replace only a stuck Loading: Offline/Ready (UpdateOfflineModeUI, whose about:blank park and reload
            // land here too) own the line, as WPF writes them whether or not the engine is up.
            if (!BrowserWebHost.HasEngine && BrowserStatusLoading()) SetBrowserFailure(BrowserWebHost.FailureStatusKey);
        }

        private bool BrowserStatusLoading() => string.Equals(IconText.Bare(TxtBrowserStatus.Text).Trim(),
            IconText.BareLoc("label_loading").Trim(), StringComparison.OrdinalIgnoreCase);

        private void SetBrowserFailure(string key)
        {
            TxtBrowserStatus.Bind(TextBlock.TextProperty, LocBinding(key));
            TxtBrowserStatus.Foreground = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.FromRgb(255, 107, 107));
        }

        /// <summary>WPF BrowserLoadingText_Click: initially load the mod's default. Subsequent Reload uses the selected
        /// site's home, behind the offline gate (WPF #867).</summary>
        internal void OnBrowserPromptClick()
        {
            if (Shell?.IsBrowserPoppedOut == true) Shell.BtnPopOutBrowser_Click(this, new RoutedEventArgs());
            else if (BrowserWebHost.Source == null) Shell?.LoadInitialBrowserHome();
            else Shell?.BtnReloadBrowser_Click(this, new RoutedEventArgs());
        }

        internal void SyncBrowserPopout()
        {
            // WPF MainWindow.Browser.cs:2790-2800: label and tooltip follow the hosting window.
            bool popped = Shell?.IsBrowserPoppedOut == true;
            if (BtnPopOutBrowser.Content is TextBlock label)
                label.Bind(TextBlock.TextProperty, (Binding)new StrBareExtension(
                    popped ? "btn_focus" : "btn_pop_out").ProvideValue(null!));
            BtnPopOutBrowser.Bind(ToolTip.TipProperty, LocBinding(popped
                ? "tooltip_browser_is_popped_out_click_to_focus" : "tooltip_pop_out_browser_to_resizable_window"));
            SyncBrowserPrompt();
        }

        private void BtnPopOutBrowser_Click(object? sender, RoutedEventArgs e) => Shell?.BtnPopOutBrowser_Click(sender, e);
        private void ToggleEnhanceIfPossible_Changed(object? sender, RoutedEventArgs e) { } // greyed: see the header
    }
}
