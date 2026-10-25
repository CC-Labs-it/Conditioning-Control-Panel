using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Webcam;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class DeeperPlayerWebcamTests
{
    private static Enhancement CameraEnhancement() => new()
    {
        MediaType = MediaTypes.Video, MediaSource = "*",
        Rules = new() { new EnhancementRule { Trigger = new BlinkDetectedTrigger() } }
    };

    private static EnhancementPlayerWindow Player()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var win = new EnhancementPlayerWindow(CameraEnhancement(), "test");
        win.WebcamAvailable = () => true;
        win.WebcamRunning = () => false;
        win.Show();
        return win;
    }

    private sealed class Audio : IDeeperLocalAudio
    {
        public double DurationSeconds => 60;
        public double PositionSeconds { get; set; }
        public int Volume { get; set; }
        public bool Playing;
        public event Action? Ended { add { } remove { } }
        public void Play() => Playing = true;
        public void Pause() => Playing = false;
        public void Dispose() { }
    }

    [Fact]
    public Task RealMediaPathOffersWebcamWithoutDelayingPlaybackAndCloseStopsAnOwnedCamera() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var settings = CoreSettings.Current;
        var saved = (settings.WebcamConsentGiven, settings.WebcamConsentVersion);
        var open = DeeperLocalAudio.Open;
        var audio = new Audio();
        DeeperLocalAudio.Open = _ => Task.FromResult<IDeeperLocalAudio?>(audio);
        var win = Player();
        int stops = 0;
        win.StartWebcam = () => Task.FromResult(true);
        win.StopWebcam = () => { stops++; return Task.CompletedTask; };
        try
        {
            settings.WebcamConsentGiven = true;
            settings.WebcamConsentVersion = WebcamConsent.ConsentVersion;
            win.OpenLocalMediaFile("webcam-test.mp3");
            Dispatcher.UIThread.RunJobs();
            Assert.True(audio.Playing);
            Assert.True(win.FindControl<Border>("WebcamBanner")!.IsVisible);
            await win.EnableWebcamBannerAsync();
            Assert.False(win.FindControl<Border>("WebcamBanner")!.IsVisible);
            win.Close();
            Assert.Equal(1, stops);
        }
        finally
        {
            win.Close();
            DeeperLocalAudio.Open = open;
            (settings.WebcamConsentGiven, settings.WebcamConsentVersion) = saved;
        }
    });

    [Fact]
    public Task OfferIsNonblockingDismissibleAndRearmedOnLoadAndConsentIsRequired() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var settings = CoreSettings.Current;
        var saved = (settings.WebcamConsentGiven, settings.WebcamConsentVersion);
        var win = Player();
        int starts = 0;
        win.StartWebcam = () => { starts++; return Task.FromResult(true); };
        try
        {
            settings.WebcamConsentGiven = false;
            win.MaybePromptForWebcamBeforePlay();
            var banner = win.FindControl<Border>("WebcamBanner")!;
            Assert.True(banner.IsVisible);
            Assert.Equal(0, starts);
            win.FindControl<Button>("BtnWebcamBannerDismiss")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            win.MaybePromptForWebcamBeforePlay();
            Assert.False(banner.IsVisible);
            win.LoadEnhancementFromMemory(CameraEnhancement(), "test");
            win.MaybePromptForWebcamBeforePlay();
            Assert.True(banner.IsVisible);
            await win.EnableWebcamBannerAsync();
            Assert.Equal(0, starts);
            Assert.Equal(Loc.Get("deeper_player_eye_tracking_first_time"), win.FindControl<TextBlock>("TxtWebcamBanner")!.Text);
            Assert.False(win.FindControl<Button>("BtnWebcamBannerEnable")!.IsVisible);
            win.LoadEnhancementFromMemory(new Enhancement(), "test");
            Assert.False(banner.IsVisible);
            Assert.False(win.FindControl<Button>("BtnEyeTracking")!.IsVisible);
        }
        finally { win.Close(); (settings.WebcamConsentGiven, settings.WebcamConsentVersion) = saved; }
    });

    [Fact]
    public Task ToggleRequiresConfirmationReportsFailureAndStopsWithoutAnotherPrompt() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var settings = CoreSettings.Current;
        var saved = (settings.WebcamConsentGiven, settings.WebcamConsentVersion);
        var win = Player();
        bool running = false, accept = false, succeed = false;
        int starts = 0, stops = 0, messages = 0;
        win.WebcamRunning = () => running;
        win.StartWebcam = () => { starts++; running = succeed; return Task.FromResult(succeed); };
        win.StopWebcam = () => { stops++; running = false; return Task.CompletedTask; };
        win.WebcamMessage = (_, confirm) => { messages++; return Task.FromResult(confirm && accept); };
        try
        {
            settings.WebcamConsentGiven = false;
            await win.TogglePlayerWebcamAsync();
            Assert.Equal(0, starts);
            settings.WebcamConsentGiven = true;
            settings.WebcamConsentVersion = WebcamConsent.ConsentVersion;
            await win.TogglePlayerWebcamAsync();
            Assert.Equal(0, starts);
            accept = true;
            await win.TogglePlayerWebcamAsync();
            Assert.Equal(1, starts);
            Assert.Equal(4, messages); // first-time, declined, accepted, failed
            succeed = true;
            await win.TogglePlayerWebcamAsync();
            Assert.Equal("deeper_player_btn_eye_tracking_stop", Assert.IsType<IconLabel>(win.FindControl<Button>("BtnEyeTracking")!.Content).Key);
            await win.TogglePlayerWebcamAsync();
            Assert.Equal(1, stops);
            Assert.Equal(5, messages);
            Assert.Equal("deeper_player_btn_eye_tracking_start", Assert.IsType<IconLabel>(win.FindControl<Button>("BtnEyeTracking")!.Content).Key);
        }
        finally { win.Close(); (settings.WebcamConsentGiven, settings.WebcamConsentVersion) = saved; }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task LateStartAfterCloseOrPanicIsStopped(bool panic) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var settings = CoreSettings.Current;
        var saved = (settings.WebcamConsentGiven, settings.WebcamConsentVersion);
        var win = Player();
        var pending = new TaskCompletionSource<bool>();
        int stops = 0;
        win.StartWebcam = () => pending.Task;
        win.StopWebcam = () => { stops++; return Task.CompletedTask; };
        try
        {
            settings.WebcamConsentGiven = true;
            settings.WebcamConsentVersion = WebcamConsent.ConsentVersion;
            var start = win.EnableWebcamBannerAsync();
            Assert.False(win.FindControl<Button>("BtnWebcamBannerEnable")!.IsEnabled);
            if (panic) EnhancementPlayerWindow.StopAllAudioForPanic(); else win.Close();
            Assert.Equal(1, stops);
            pending.SetResult(true);
            await start;
            Assert.Equal(2, stops);
        }
        finally { win.Close(); (settings.WebcamConsentGiven, settings.WebcamConsentVersion) = saved; }
    });

    [Fact]
    public Task CloseLeavesAPreexistingCameraRunning() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var win = Player();
        win.WebcamRunning = () => true;
        int stops = 0;
        win.StopWebcam = () => { stops++; return Task.CompletedTask; };
        win.MaybePromptForWebcamBeforePlay();
        Assert.False(win.FindControl<Border>("WebcamBanner")!.IsVisible);
        win.Close();
        Assert.Equal(0, stops);
        return Task.CompletedTask;
    });
}
