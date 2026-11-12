using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Platform.Deeper;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Deeper;
using ConditioningControlPanel.Services.Webcam;

namespace ConditioningControlPanel.Avalonia.Views.Deeper;

public partial class EnhancementPlayerWindow
{
    private static readonly Lazy<TrackerEnhancementWebcam> WebcamAdapter = new(() => new(WebcamTracker.Instance));
    private Enhancement? _webcamEnhancement;
    private bool _webcamPromptShown, _playerStartedWebcam, _webcamClosed, _webcamStarting, _webcamBannerStarting;
    private Button EyeButton => this.FindControl<Button>("BtnEyeTracking")!;
    private Border CameraBanner => this.FindControl<Border>("WebcamBanner")!;
    private Button CameraEnable => this.FindControl<Button>("WebcamEnable")!;
    private TextBlock CameraText => this.FindControl<TextBlock>("WebcamBannerText")!;

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        EnhancementHostService.WebcamProvider ??= () => WebcamAdapter.Value;
        WebcamTracker.Instance.StateChanged += WebcamStateChanged;
        if (_uiTimer != null) _uiTimer.Tick += WebcamTick;
        WebcamStateChanged();
        WebcamTick(null, EventArgs.Empty);
    }

    protected override void OnClosed(EventArgs e)
    {
        _webcamClosed = true;
        WebcamTracker.Instance.StateChanged -= WebcamStateChanged;
        if (_uiTimer != null) _uiTimer.Tick -= WebcamTick;
        if (_playerStartedWebcam || _webcamBannerStarting) _ = WebcamTracker.Instance.StopAsync();
        base.OnClosed(e);
    }

    private void WebcamStateChanged() => EyeButton.Content = IconLabel.ForKey((WebcamStatus.IsLive(WebcamTracker.Instance.State) || WebcamTracker.Instance.State == WebcamTrackingState.Starting)
        ? "deeper_player_btn_eye_tracking_stop" : "deeper_player_btn_eye_tracking_start");

    private void WebcamTick(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(_webcamEnhancement, _loadedEnhancement))
        {
            _webcamEnhancement = _loadedEnhancement;
            _webcamPromptShown = false;
            CameraBanner.IsVisible = false;
        }
        bool needed = EnhancementCapabilities.NeedsWebcam(_loadedEnhancement);
        EyeButton.IsVisible = needed;
    }

    private void WebcamPlay_Click(object? sender, RoutedEventArgs e) => OfferWebcamBeforePlay();

    private void OfferWebcamBeforePlay()
    {
        if (_isPlaying || (_isVideoMode ? !_videoNavigated : _audioPath == null)) return;
        WebcamTick(null, EventArgs.Empty);
        if (!EnhancementCapabilities.NeedsWebcam(_loadedEnhancement) || _webcamPromptShown) return;
        _webcamPromptShown = true;
        if (WebcamTracker.Instance.IsRunning || WebcamTracker.Instance.IsStarting) return;
        CameraText.Text = Loc.Get("deeper_player_webcam_banner_text");
        CameraEnable.IsVisible = true;
        CameraBanner.IsVisible = true;
    }

    private async void EyeTracking_Click(object? sender, RoutedEventArgs e)
    {
        var tracker = WebcamTracker.Instance;
        if (tracker.IsRunning || tracker.IsStarting) { await tracker.StopAsync(); return; }
        string title = IconText.BareLoc("deeper_player_btn_eye_tracking_start");
        if (!WebcamConsent.IsCurrent(CoreSettings.Current))
        {
            await MessageDialog.ShowAsync(this, title, Loc.Get("deeper_player_eye_tracking_first_time"));
            return;
        }
        if (!await MessageDialog.ConfirmAsync(this, title, Loc.Get("deeper_player_eye_tracking_confirm_start"))) return;
        if (_webcamClosed) return;
        if (!await StartPlayerWebcam(fromBanner: false))
            await MessageDialog.ShowAsync(this, title, string.Format(Loc.Get("deeper_player_eye_tracking_start_failed_fmt"), tracker.LastError ?? tracker.State.ToString()));
    }

    private void WebcamDismiss_Click(object? sender, RoutedEventArgs e) => CameraBanner.IsVisible = false;
    private async void WebcamEnable_Click(object? sender, RoutedEventArgs e)
    {
        if (!WebcamConsent.IsCurrent(CoreSettings.Current))
        {
            CameraText.Text = Loc.Get("deeper_player_eye_tracking_first_time");
            CameraEnable.IsVisible = false;
            return;
        }
        CameraEnable.IsEnabled = false;
        try
        {
            if (await StartPlayerWebcam(fromBanner: true)) CameraBanner.IsVisible = false;
            else CameraText.Text = string.Format(Loc.Get("deeper_player_eye_tracking_start_failed_fmt"), WebcamTracker.Instance.LastError ?? WebcamTracker.Instance.State.ToString());
        }
        finally { CameraEnable.IsEnabled = true; }
    }

    private async Task<bool> StartPlayerWebcam(bool fromBanner)
    {
        if (_webcamStarting || WebcamTracker.Instance.IsRunning) return false;
        _webcamStarting = true;
        _webcamBannerStarting = fromBanner;
        try
        {
            bool started = await WebcamTracker.Instance.StartAsync();
            if (started && fromBanner && _webcamClosed) { await WebcamTracker.Instance.StopAsync(); return false; }
            if (started && fromBanner) _playerStartedWebcam = true;
            return started;
        }
        finally { _webcamStarting = false; _webcamBannerStarting = false; }
    }
}
