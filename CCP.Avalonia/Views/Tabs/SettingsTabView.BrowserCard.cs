// PORTED from WPF 7.1.5, the Home browser card. IMPORTED-AND-ADAPTED from trunk e3109afdf / 376305e62:
//   BtnMuteBrowser_Click + SyncBrowserMuteIcon     MainWindow.Browser.cs:2603, :2613
//   BtnWebcamTracking_Click + RefreshBrowserWebcamButton   MainWindow.DeeperTab.cs:245, :228
//   ChkForceShowBambiCloud_Changed                 MainWindow.DeeperTab.cs:505
//   BrowserLoadingText_Click                       MainWindow.Browser.cs:280
// Mute rides the WebHost script channel (Views/Controls/WebHostMedia.cs) and is re-applied after every navigation,
// since a new document has no flag. The load prompt: WPF builds the WebView2 on that click; here the engine exists
// from the start but loads nothing until asked, so the prompt stands in the slot until the first page.
// "Enhance if possible" is greyed because this head has no BrowserEnhanceBridge.

using System;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Localization;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class SettingsTabView
    {
        private bool _browserCardSyncing, _browserPoppedOut, _browserFullscreen;

        /// <summary>Tests: how many times the mute script was sent to the page.</summary>
        internal int BrowserMuteApplies { get; private set; }

        private void HookBrowserCard()
        {
            BrowserWebHost.NavigationCompleted += _ => ApplyBrowserMute();
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
            ToggleEnhanceIfPossible.IsEnabled = false;
            ToggleEnhanceIfPossible.Bind(ToolTip.TipProperty, LocBinding("exclusives_not_on_this_build"));
            ToolTip.SetShowOnDisabled(ToggleEnhanceIfPossible, true);
            SyncBrowserCard();
        }

        private void OnBrowserCardTrackerChanged() => Dispatcher.UIThread.Post(RefreshBrowserWebcamButton);
        private void OnBrowserCardModChanged(object? sender, ModPackage? mod) => Dispatcher.UIThread.Post(SyncBrowserCard);

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
            bool modWants = true;
            try { modWants = App.Mods?.ShowBambiCloudOption() ?? true; }
            catch (Exception ex) { Log.Debug("ShowBambiCloudOption: {E}", ex.Message); }
            bool show = modWants || CoreSettings.Current.ForceShowBambiCloud;
            RbBambiCloud.IsVisible = show;
            if (show || RbBambiCloud.IsChecked != true) return;
            RbHypnoTube.IsChecked = true;
            if (navigateIfHidden) Shell?.BrowserSiteToggle_Click(RbHypnoTube, new RoutedEventArgs());
        }

        // ------------------------------------------------------------------ the load prompt

        /// <summary>The prompt stands in the slot while an engine is there and nothing was ever loaded. With no engine
        /// the host's own panel says why, so the prompt stays out of its way.</summary>
        internal static bool BrowserPromptShown(bool hasEngine, bool loaded) => hasEngine && !loaded;

        internal void SyncBrowserPrompt()
        {
            bool prompt = BrowserPromptShown(BrowserWebHost.HasEngine, BrowserWebHost.Source != null);
            bool offline = CoreSettings.Current.OfflineMode;
            BrowserLoadingText.IsVisible = offline || _browserPoppedOut || _browserFullscreen || prompt;
            BrowserWebHost.IsVisible = _browserPoppedOut || (!offline && !prompt);
        }

        internal void SetBrowserFullscreen(bool fullscreen)
        {
            _browserFullscreen = fullscreen;
            SetBrowserPoppedOut(_browserPoppedOut);
        }

        // WPF MainWindow.Browser.cs:48-54,288-290: an initialized browser is not reloaded.
        internal void OnBrowserPromptClick()
        {
            if (_browserPoppedOut) Shell?.PopOutBrowser();
            else Shell?.BtnReloadBrowser_Click(this, new RoutedEventArgs());
        }

        // WPF MainWindow.Browser.cs:2736-2796: the card names the other window and returns to its load prompt on close.
        internal void SetBrowserPoppedOut(bool poppedOut)
        {
            _browserPoppedOut = poppedOut;
            BrowserLoadingText.Bind(TextBlock.TextProperty, LocBareBinding(CoreSettings.Current.OfflineMode
                ? "label_browser_disabled_in_offline_mode" : _browserFullscreen && !poppedOut
                    ? "label_browser_in_fullscreen" : poppedOut
                    ? "label_browser_popped_out_nclick_to_focus_window" : "label_click_to_load_browser"));
            PopOutBrowserIcon.Kind = poppedOut ? IconKind.Window : IconKind.Open;
            TxtPopOutBrowser.Bind(TextBlock.TextProperty, LocBareBinding(poppedOut ? "btn_focus" : "btn_pop_out"));
            if (!CoreSettings.Current.OfflineMode)
                BtnPopOutBrowser.Bind(ToolTip.TipProperty, LocBinding(poppedOut
                    ? "tooltip_browser_is_popped_out_click_to_focus" : "tooltip_pop_out_browser_to_resizable_window"));
            SyncBrowserPrompt();
        }

        private static Binding LocBareBinding(string key) => (Binding)new StrBareExtension(key).ProvideValue(null!);

        private void BtnPopOutBrowser_Click(object? sender, RoutedEventArgs e) => Shell?.PopOutBrowser();
        private void ToggleEnhanceIfPossible_Changed(object? sender, RoutedEventArgs e) { } // greyed: see the header
    }
}
