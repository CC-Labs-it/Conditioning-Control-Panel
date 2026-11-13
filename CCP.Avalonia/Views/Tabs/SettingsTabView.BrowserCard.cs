// PORTED from WPF 7.1.5, the Home browser card. IMPORTED-AND-ADAPTED from trunk e3109afdf / 376305e62:
//   BtnMuteBrowser_Click + SyncBrowserMuteIcon     MainWindow.Browser.cs:2603, :2613
//   BtnWebcamTracking_Click + RefreshBrowserWebcamButton   MainWindow.DeeperTab.cs:245, :228
//   ChkForceShowBambiCloud_Changed                 MainWindow.DeeperTab.cs:505
//   BrowserLoadingText_Click                       MainWindow.Browser.cs:280
// Mute rides the WebHost script channel (Views/Controls/WebHostMedia.cs) and is re-applied after every navigation,
// since a new document has no flag. The load prompt: WPF builds the WebView2 on that click; here the engine exists
// from the start but loads nothing until asked, so the prompt stands in the slot until the first page.
// Pop out preserves the native adapter with BeginReparenting.

using System;
using Avalonia.Controls;
using Avalonia.Data;
using ConditioningControlPanel.Avalonia.Localization;
using Avalonia.Interactivity;
using Avalonia.Threading;
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
        /// <summary>The mod the card last followed: WPF only re-syncs the site on a real switch (ApplyActiveModChange),
        /// while this head's ModChanged also fires on a same-mod asset refresh (#4330).</summary>
        private string? _browserCardModId;


        /// <summary>Tests: how many times the mute script was sent to the page.</summary>
        internal int BrowserMuteApplies { get; private set; }

        private void HookBrowserCard()
        {
            BrowserWebHost.RouteBrowserPopups = true; // WPF BrowserService.cs:538-562
            BrowserWebHost.BrowserProcessFailed += OnBrowserCrash;
            BrowserWebHost.NavigationCompleted += _ => ApplyBrowserMute();
            BrowserWebHost.NavigationFinished += (url, success) =>
                _ = Browser.BrowserAdCss.ApplyAsync(url, success, BrowserWebHost.InvokeScriptAsync);
            BrowserWebHost.PropertyChanged += (_, e) => { if (e.Property == WebHost.SourceProperty) SyncBrowserPrompt(); };
            AttachedToVisualTree += (_, _) =>
            {
                Platform.WebcamTracker.Instance.StateChanged -= OnBrowserCardTrackerChanged;
                Platform.WebcamTracker.Instance.StateChanged += OnBrowserCardTrackerChanged;
                _browserCardModId = App.Mods?.ActiveModId;
                CoreMods.ModChanged -= OnBrowserCardModChanged;
                CoreMods.ModChanged += OnBrowserCardModChanged;
                SyncBrowserCard();
            };
            DetachedFromVisualTree += (_, _) =>
            {
                Platform.WebcamTracker.Instance.StateChanged -= OnBrowserCardTrackerChanged;
                CoreMods.ModChanged -= OnBrowserCardModChanged;
            };
            HookBrowserEnhance();
            SyncBrowserCard();
        }

        private void OnBrowserCardTrackerChanged() => Dispatcher.UIThread.Post(RefreshBrowserWebcamButton);
        private void OnBrowserCardModChanged(object? sender, ModPackage? mod) => Dispatcher.UIThread.Post(() =>
        {
            var active = App.Mods?.ActiveModId;
            var switched = !string.Equals(_browserCardModId, active, StringComparison.OrdinalIgnoreCase);
            _browserCardModId = active;
            // A same-mod asset refresh does not own the page or the radios: the user keeps what is open.
            if (switched) OnBrowserModSwitched();
        });

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
            try { ChkForceShowBambiCloud.IsChecked = CoreSettings.Current.ForceShowBambiCloud; ToggleEnhanceIfPossible.IsChecked = CoreSettings.Current.BrowserEnhanceIfPossible; }
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
        internal async void BtnWebcamTracking_Click(object? sender, RoutedEventArgs e)
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
            bool prompt = _browserCrashed || popped || offline || BrowserPromptShown(BrowserWebHost.HasEngine, BrowserWebHost.Source != null);
            BrowserLoadingText.IsVisible = prompt;
            BrowserLoadingText.Bind(TextBlock.TextProperty, (Binding)new StrBareExtension(_browserCrashed
                ? "label_browser_crashed_restart" : offline
                ? "label_browser_disabled_in_offline_mode" : popped   // WPF MainWindow.Browser.cs:2737
                ? "label_browser_popped_out_nclick_to_focus_window" : "label_click_to_load_browser").ProvideValue(null!));
            // A native web view draws over anything laid on it, so the prompt only shows with the host hidden.
            // The placeholder belongs to the card; the popped-out host stays visible in its window.
            BrowserWebHost.IsVisible = !_browserCrashed && (popped || !prompt);
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
    }
}
