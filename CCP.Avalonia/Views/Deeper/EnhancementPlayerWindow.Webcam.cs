using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Deeper;
using ConditioningControlPanel.Services.Webcam;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Deeper;

public partial class EnhancementPlayerWindow
{
    // Per-window verbs allow headless tests to exercise consent/lifetime without opening a camera.
    internal Func<bool> WebcamAvailable = () => CoreWebcam.IsAvailable;
    internal Func<bool> WebcamRunning = () => WebcamTracker.Instance.IsRunning;
    internal Func<Task<bool>> StartWebcam = () => WebcamTracker.Instance.StartAsync();
    internal Func<Task> StopWebcam = () => WebcamTracker.Instance.StopAsync();
    internal Func<string, bool, Task<bool>> WebcamMessage = null!;
    private bool _webcamPromptShown, _playerStartedWebcam, _webcamStartPending;
    private int _webcamGeneration;

    private void InitializeWebcamControls()
    {
        WebcamMessage = (message, confirm) => confirm
            ? MessageDialog.ConfirmAsync(this, IconText.BareLoc("deeper_player_btn_eye_tracking_start"), message)
            : MessageDialog.ShowAsync(this, IconText.BareLoc("deeper_player_btn_eye_tracking_start"), message);
        this.FindControl<Button>("BtnWebcamBannerEnable")!.Click += async (_, _) => await EnableWebcamBannerAsync();
        this.FindControl<Button>("BtnWebcamBannerDismiss")!.Click += (_, _) =>
            this.FindControl<Border>("WebcamBanner")!.IsVisible = false;
        WebcamTracker.Instance.StateChanged += RefreshWebcamButton;
        WebcamTracker.Instance.OnStartupProgress += WebcamStartupProgress;
        RefreshWebcamButton();
    }

    private void WebcamStartupProgress(double progress, string status) => RefreshWebcamButton();

    internal void RefreshWebcamButton()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(RefreshWebcamButton); return; }
        if (_isClosing) return;
        var button = this.FindControl<Button>("BtnEyeTracking")!;
        button.IsVisible = EnhancementCapabilities.NeedsWebcam(_loadedEnhancement);
        button.Content = IconLabel.ForKey(WebcamRunning() || _webcamStartPending || WebcamTracker.Instance.IsStarting
            ? "deeper_player_btn_eye_tracking_stop" : "deeper_player_btn_eye_tracking_start");
    }

    private void ResetWebcamPrompt()
    {
        _webcamPromptShown = false;
        this.FindControl<Border>("WebcamBanner")!.IsVisible = false;
        RefreshWebcamButton();
    }

    internal void MaybePromptForWebcamBeforePlay()
    {
        if (_isClosing || _webcamPromptShown || !EnhancementCapabilities.NeedsWebcam(_loadedEnhancement)) return;
        _webcamPromptShown = true;
        if (!WebcamAvailable() || WebcamRunning()) return;
        this.FindControl<TextBlock>("TxtWebcamBanner")!.Text = Loc.Get("deeper_player_webcam_banner_text");
        this.FindControl<Button>("BtnWebcamBannerEnable")!.IsVisible = true;
        this.FindControl<Border>("WebcamBanner")!.IsVisible = true;
    }

    internal async Task EnableWebcamBannerAsync()
    {
        if (_isClosing || _webcamStartPending) return;
        if (!WebcamAvailable()) { this.FindControl<Border>("WebcamBanner")!.IsVisible = false; return; }
        if (!WebcamConsent.IsCurrent(CoreSettings.Current))
        {
            this.FindControl<TextBlock>("TxtWebcamBanner")!.Text = Loc.Get("deeper_player_eye_tracking_first_time");
            this.FindControl<Button>("BtnWebcamBannerEnable")!.IsVisible = false;
            return;
        }
        await StartPlayerWebcamAsync(banner: true);
    }

    private async void BtnEyeTracking_Click() => await TogglePlayerWebcamAsync();

    internal async Task TogglePlayerWebcamAsync()
    {
        if (_isClosing) return;
        if (!WebcamAvailable()) { await WebcamMessage(Loc.Get("deeper_player_eye_tracking_unavailable"), false); return; }
        if (WebcamRunning() || _webcamStartPending || WebcamTracker.Instance.IsStarting)
        {
            StopPlayerWebcam(force: true);
            return;
        }
        if (!WebcamConsent.IsCurrent(CoreSettings.Current))
        {
            await WebcamMessage(Loc.Get("deeper_player_eye_tracking_first_time"), false);
            return;
        }
        var generation = _webcamGeneration;
        if (!await WebcamMessage(Loc.Get("deeper_player_eye_tracking_confirm_start"), true)) return;
        if (_isClosing || generation != _webcamGeneration || !WebcamConsent.IsCurrent(CoreSettings.Current)) return;
        await StartPlayerWebcamAsync(banner: false);
    }

    private async Task StartPlayerWebcamAsync(bool banner)
    {
        if (_webcamStartPending || WebcamRunning()) return;
        _webcamStartPending = true;
        var generation = _webcamGeneration;
        this.FindControl<Button>("BtnWebcamBannerEnable")!.IsEnabled = false;
        RefreshWebcamButton();
        try
        {
            var started = await StartWebcam();
            if (_isClosing || generation != _webcamGeneration)
            {
                if (started) await StopWebcam();
                return;
            }
            if (started)
            {
                _playerStartedWebcam = true;
                this.FindControl<Border>("WebcamBanner")!.IsVisible = false;
            }
            // WPF shows the state name (.xaml.cs:807,910), not the tracker's English LastError.
            else await ReportWebcamFailure(banner, WebcamTracker.Instance.StartWasStopped ? "Stopped" : "Error");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "EnhancementPlayer: webcam start failed");
            if (!_isClosing && generation == _webcamGeneration) await ReportWebcamFailure(banner, "Error");
        }
        finally
        {
            _webcamStartPending = false;
            this.FindControl<Button>("BtnWebcamBannerEnable")!.IsEnabled = true;
            RefreshWebcamButton();
        }
    }

    private async Task ReportWebcamFailure(bool banner, string state)
    {
        var message = string.Format(Loc.Get("deeper_player_eye_tracking_start_failed_fmt"), state);
        if (banner) this.FindControl<TextBlock>("TxtWebcamBanner")!.Text = message;
        else await WebcamMessage(message, false);
    }

    private async void StopPlayerWebcam(bool force = false)
    {
        _webcamGeneration++;
        if (!force && !_playerStartedWebcam && !_webcamStartPending) return;
        _playerStartedWebcam = false;
        try { await StopWebcam(); }
        catch (Exception ex) { Log.Warning(ex, "EnhancementPlayer: webcam stop failed"); }
        RefreshWebcamButton();
    }

    private void CloseWebcamControls()
    {
        WebcamTracker.Instance.StateChanged -= RefreshWebcamButton;
        WebcamTracker.Instance.OnStartupProgress -= WebcamStartupProgress;
        // ponytail: own explicit button starts too, so every camera started here is stopped on close.
        StopPlayerWebcam();
    }
}
