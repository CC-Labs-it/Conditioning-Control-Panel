using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Platform.Deeper;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using ConditioningControlPanel.Services.Webcam;
using OpenCvSharp;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class DeeperPlayerWebcamTests
{
    private sealed class Source : IFrameSource
    {
        public int Opens;
        public bool OpenResult = true;
        public Action? Opening;
        public bool Open() { Opens++; Opening?.Invoke(); return OpenResult; }
        public bool Read(Mat frame) { frame.Create(480, 640, MatType.CV_8UC3); frame.SetTo(Scalar.All(0)); System.Threading.Thread.Sleep(10); return true; }
        public void Dispose() { }
    }
    private sealed class Audio : IDeeperLocalAudio
    {
        public int Volume { get; set; }
        public double DurationSeconds => 90;
        public double? PlaybackDurationSeconds => null;
        public double PositionSeconds { get; set; }
        public void Play() { }
        public void Pause() { }
        public event Action? Ended { add { } remove { } }
        public event Action? Failed { add { } remove { } }
        public void Dispose() { }
    }
    private static void PrepareAudio(EnhancementPlayerWindow w)
    {
        typeof(EnhancementPlayerWindow).GetField("_audioPath", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(w, "test.mp3");
        typeof(EnhancementPlayerWindow).GetField("_localAudio", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(w, new Audio());
    }
    private static void Play(EnhancementPlayerWindow w) => Click(w.FindControl<Button>("BtnPlayPause")!);
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Tick(EnhancementPlayerWindow w) => typeof(EnhancementPlayerWindow).GetMethod("WebcamTick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, new object?[] { null, EventArgs.Empty });
    private static Enhancement WebcamEnhancement() => new() { MediaType = MediaTypes.Audio, Metadata = new EnhancementMetadata { AutoTags = { EnhancementAutoTagger.TagWebcam } } };
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
    [Fact]
    public Task ConsentAndConfirmationGateCamera() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var settings = CoreSettings.Current;
        var old = (settings.WebcamConsentGiven, settings.WebcamConsentVersion, WebcamTracker.SourceFactory, EnhancementHostService.WebcamProvider);
        var source = new Source();
        WebcamTracker.SourceFactory = () => source;
        var player = new EnhancementPlayerWindow(WebcamEnhancement(), null);
        try
        {
            settings.WebcamConsentGiven = false;
            settings.WebcamConsentVersion = "";
            player.Show();
            Dispatcher.UIThread.RunJobs();
            var button = player.FindControl<Button>("BtnEyeTracking")!;
            Assert.True(button.IsVisible);
            var label = Assert.IsType<IconLabel>(button.Content);
            Assert.Equal("deeper_player_btn_eye_tracking_start", label.Key);
            Assert.Equal(ConditioningControlPanel.Localization.Loc.Get(label.Key), label.Source);
            Assert.IsType<IconGlyph>(label.Children[0]);
            Click(button);
            await Task.Delay(100);
            var message = Assert.Single(player.OwnedWindows.OfType<MessageDialog>());
            Assert.Equal(IconText.BareLoc("deeper_player_btn_eye_tracking_start"), message.Title);
            Assert.Equal(0, source.Opens);
            message.Close(false);
            settings.WebcamConsentGiven = true;
            settings.WebcamConsentVersion = WebcamConsent.ConsentVersion;
            Click(button);
            await Task.Delay(100);
            message = Assert.Single(player.OwnedWindows.OfType<MessageDialog>());
            Assert.Equal(0, source.Opens);
            message.Close(false);
            await Task.Delay(100);
            Assert.Equal(0, source.Opens);
        }
        finally
        {
            player.Close();
            WebcamTracker.Instance.Stop();
            (settings.WebcamConsentGiven, settings.WebcamConsentVersion, WebcamTracker.SourceFactory, EnhancementHostService.WebcamProvider) = old;
        }
    });
    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++) await Task.Delay(50);
        Assert.True(condition());
    }
    [Fact]
    public Task AcceptedStartUpdatesLabelAndCloseRespectsOwnership() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var settings = CoreSettings.Current;
        var old = (settings.WebcamConsentGiven, settings.WebcamConsentVersion, WebcamTracker.SourceFactory, EnhancementHostService.WebcamProvider);
        var source = new Source();
        WebcamTracker.SourceFactory = () => source;
        settings.WebcamConsentGiven = true;
        settings.WebcamConsentVersion = WebcamConsent.ConsentVersion;
        var player = new EnhancementPlayerWindow(WebcamEnhancement(), null);
        try
        {
            player.Show();
            Click(player.FindControl<Button>("BtnEyeTracking")!);
            await Until(() => player.OwnedWindows.OfType<MessageDialog>().Any());
            Assert.Equal(0, source.Opens);
            Assert.Single(player.OwnedWindows.OfType<MessageDialog>()).Close(true);
            await Until(() => WebcamTracker.Instance.IsRunning);
            await Until(() => (player.FindControl<Button>("BtnEyeTracking")!.Content as IconLabel)?.Key == "deeper_player_btn_eye_tracking_stop");
            var stopLabel = Assert.IsType<IconLabel>(player.FindControl<Button>("BtnEyeTracking")!.Content);
            Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("deeper_player_btn_eye_tracking_stop"), stopLabel.Source);
            Assert.IsType<IconGlyph>(stopLabel.Children[0]);
            player.Close();
            await Task.Delay(300);
            Assert.True(WebcamTracker.Instance.IsRunning);
            var second = new EnhancementPlayerWindow(WebcamEnhancement(), null);
            try
            {
                second.Show();
                PrepareAudio(second);
                Play(second);
                Assert.False(second.FindControl<Border>("WebcamBanner")!.IsVisible);
            }
            finally { second.Close(); }
            await Task.Delay(300);
            Assert.True(WebcamTracker.Instance.IsRunning);
            await WebcamTracker.Instance.StopAsync();
            var third = new EnhancementPlayerWindow(WebcamEnhancement(), null);
            try
            {
                third.Show();
                PrepareAudio(third);
                Play(third);
                Assert.True(third.FindControl<Border>("WebcamBanner")!.IsVisible);
                Click(third.FindControl<Button>("WebcamEnable")!);
                await Until(() => WebcamTracker.Instance.IsRunning);
            }
            finally { third.Close(); }
            await Until(() => !WebcamTracker.Instance.IsRunning);
        }
        finally
        {
            player.Close();
            await WebcamTracker.Instance.StopAsync();
            (settings.WebcamConsentGiven, settings.WebcamConsentVersion, WebcamTracker.SourceFactory, EnhancementHostService.WebcamProvider) = old;
        }
    });
    [Fact]
    public Task BannerOwnershipSurvivesButtonStopAndRestart() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var settings = CoreSettings.Current;
        var old = (settings.WebcamConsentGiven, settings.WebcamConsentVersion, WebcamTracker.SourceFactory, EnhancementHostService.WebcamProvider);
        WebcamTracker.SourceFactory = () => new Source();
        settings.WebcamConsentGiven = true;
        settings.WebcamConsentVersion = WebcamConsent.ConsentVersion;
        var player = new EnhancementPlayerWindow(WebcamEnhancement(), null);
        try
        {
            player.Show(); PrepareAudio(player); Play(player);
            Assert.True(player.FindControl<Border>("WebcamBanner")!.IsVisible);
            Click(player.FindControl<Button>("WebcamEnable")!);
            await Until(() => WebcamTracker.Instance.IsRunning && !WebcamTracker.Instance.IsStarting);
            Click(player.FindControl<Button>("BtnEyeTracking")!);
            await Until(() => !WebcamTracker.Instance.IsRunning);
            Click(player.FindControl<Button>("BtnEyeTracking")!);
            await Until(() => player.OwnedWindows.OfType<MessageDialog>().Any());
            Assert.Single(player.OwnedWindows.OfType<MessageDialog>()).Close(true);
            await Until(() => WebcamTracker.Instance.IsRunning && !WebcamTracker.Instance.IsStarting);
            player.Close();
            await Until(() => !WebcamTracker.Instance.IsRunning);
        }
        finally
        {
            player.Close(); await WebcamTracker.Instance.StopAsync();
            (settings.WebcamConsentGiven, settings.WebcamConsentVersion, WebcamTracker.SourceFactory, EnhancementHostService.WebcamProvider) = old;
        }
    });
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task CloseDuringStartRespectsOrigin(bool fromBanner) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var settings = CoreSettings.Current;
        var old = (settings.WebcamConsentGiven, settings.WebcamConsentVersion, WebcamTracker.SourceFactory, EnhancementHostService.WebcamProvider);
        using var release = new System.Threading.ManualResetEventSlim();
        var source = new Source { Opening = () => release.Wait(TimeSpan.FromSeconds(20)) };
        WebcamTracker.SourceFactory = () => source;
        settings.WebcamConsentGiven = true;
        settings.WebcamConsentVersion = WebcamConsent.ConsentVersion;
        var player = new EnhancementPlayerWindow(WebcamEnhancement(), null);
        try
        {
            player.Show();
            if (fromBanner)
            {
                PrepareAudio(player);
                Play(player);
                Assert.True(player.FindControl<Border>("WebcamBanner")!.IsVisible);
                Click(player.FindControl<Button>("WebcamEnable")!);
            }
            else
            {
                Click(player.FindControl<Button>("BtnEyeTracking")!);
                await Until(() => player.OwnedWindows.OfType<MessageDialog>().Any());
                Assert.Single(player.OwnedWindows.OfType<MessageDialog>()).Close(true);
            }
            await Until(() => source.Opens > 0);
            player.Close();
            release.Set();
            if (fromBanner)
            {
                await Until(() => !WebcamTracker.Instance.IsStarting);
                Assert.False(WebcamTracker.Instance.IsRunning);
            }
            else await Until(() => WebcamTracker.Instance.IsRunning && !WebcamTracker.Instance.IsStarting);
        }
        finally
        {
            release.Set();
            player.Close();
            await WebcamTracker.Instance.StopAsync();
            (settings.WebcamConsentGiven, settings.WebcamConsentVersion, WebcamTracker.SourceFactory, EnhancementHostService.WebcamProvider) = old;
        }
    });
    [Fact]
    public Task FailedStartReportsError() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var settings = CoreSettings.Current;
        var old = (settings.WebcamConsentGiven, settings.WebcamConsentVersion, WebcamTracker.SourceFactory, EnhancementHostService.WebcamProvider);
        var source = new Source { OpenResult = false };
        WebcamTracker.SourceFactory = () => source;
        settings.WebcamConsentGiven = true;
        settings.WebcamConsentVersion = WebcamConsent.ConsentVersion;
        var player = new EnhancementPlayerWindow(WebcamEnhancement(), null);
        try
        {
            player.Show();
            Click(player.FindControl<Button>("BtnEyeTracking")!);
            await Until(() => player.OwnedWindows.OfType<MessageDialog>().Any());
            Assert.Single(player.OwnedWindows.OfType<MessageDialog>()).Close(true);
            await Until(() => source.Opens > 0 && player.OwnedWindows.OfType<MessageDialog>().Any());
            Assert.False(WebcamTracker.Instance.IsRunning);
            Assert.Contains("Could not start", Assert.Single(player.OwnedWindows.OfType<MessageDialog>()).FindControl<TextBlock>("TxtMessage")!.Text);
        }
        finally
        {
            foreach (var dialog in player.OwnedWindows.OfType<MessageDialog>().ToArray()) dialog.Close(false);
            player.Close();
            await WebcamTracker.Instance.StopAsync();
            (settings.WebcamConsentGiven, settings.WebcamConsentVersion, WebcamTracker.SourceFactory, EnhancementHostService.WebcamProvider) = old;
        }
    });
    [Fact]
    public async Task PlayerTestsRestoreWebcamProvider()
    {
        var original = EnhancementHostService.WebcamProvider;
        try
        {
            EnhancementHostService.WebcamProvider = null;
            await ConsentAndConfirmationGateCamera();
            Assert.Null(EnhancementHostService.WebcamProvider);
            await AcceptedStartUpdatesLabelAndCloseRespectsOwnership();
            Assert.Null(EnhancementHostService.WebcamProvider);
            await BannerOwnershipSurvivesButtonStopAndRestart();
            Assert.Null(EnhancementHostService.WebcamProvider);
            await FailedStartReportsError();
            Assert.Null(EnhancementHostService.WebcamProvider);
            await CloseDuringStartRespectsOrigin(false);
            Assert.Null(EnhancementHostService.WebcamProvider);
            await CloseDuringStartRespectsOrigin(true);
            Assert.Null(EnhancementHostService.WebcamProvider);
            await BannerIsOfferedOnlyOnceAndButtonRequiresWebcam();
            Assert.Null(EnhancementHostService.WebcamProvider);
        }
        finally { EnhancementHostService.WebcamProvider = original; }
    }
    [Fact]
    public void AdapterForwardsTrackerEvents()
    {
        var tracker = new WebcamTracker();
        var adapter = new TrackerEnhancementWebcam(tracker);
        int blink = 0, mouth = 0, lost = 0, found = 0;
        double x = 0, y = 0;
        adapter.OnBlink += () => blink++;
        adapter.OnMouthOpen += () => mouth++;
        adapter.OnFaceLost += () => lost++;
        adapter.OnFaceFound += () => found++;
        adapter.OnGazeMove += (a, b) => { x = a; y = b; };
        tracker.RaiseBlinkForTest();
        tracker.RaiseMouthForTest();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        ((Action?)typeof(WebcamTracker).GetField("OnFaceLost", flags)!.GetValue(tracker))?.Invoke();
        ((Action?)typeof(WebcamTracker).GetField("OnFaceFound", flags)!.GetValue(tracker))?.Invoke();
        ((Action<global::Avalonia.Point>?)typeof(WebcamTracker).GetField("OnGazeMove", flags)!.GetValue(tracker))?.Invoke(new global::Avalonia.Point(12, 34));
        Assert.Equal((1, 1, 1, 1, 12d, 34d), (blink, mouth, lost, found, x, y));
    }
    [Fact]
    public Task BannerDoesNotCoverMedia() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var oldProvider = EnhancementHostService.WebcamProvider;
        var player = new EnhancementPlayerWindow(WebcamEnhancement(), null);
        try
        {
            player.Show(); PrepareAudio(player); Play(player); player.UpdateLayout();
            var banner = player.FindControl<Border>("WebcamBanner")!;
            var audio = player.FindControl<Border>("AudioPane")!;
            var bannerY = banner.TranslatePoint(new global::Avalonia.Point(), player)!.Value.Y;
            var audioY = audio.TranslatePoint(new global::Avalonia.Point(), player)!.Value.Y;
            Assert.True(banner.IsVisible && banner.Bounds.Height > 0 && audioY >= bannerY + banner.Bounds.Height);
        }
        finally { player.Close(); EnhancementHostService.WebcamProvider = oldProvider; }
        return Task.CompletedTask;
    });
    [Fact]
    public Task BannerIsOfferedOnlyOnceAndButtonRequiresWebcam() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var oldProvider = EnhancementHostService.WebcamProvider;
        var player = new EnhancementPlayerWindow(WebcamEnhancement(), null);
        try
        {
            player.Show();
            PrepareAudio(player);
            var banner = player.FindControl<Border>("WebcamBanner")!;
            // Playback observed from a page is not a player Play action.
            typeof(EnhancementPlayerWindow).GetField("_isPlaying", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(player, true);
            Tick(player);
            Assert.False(banner.IsVisible);
            Play(player); // pause
            Assert.False(banner.IsVisible);
            Play(player); // resume using the button
            Assert.True(banner.IsVisible);
            player.UpdateLayout();
            var audio = player.FindControl<Border>("AudioPane")!;
            var bannerY = banner.TranslatePoint(new global::Avalonia.Point(), player)!.Value.Y;
            var audioY = audio.TranslatePoint(new global::Avalonia.Point(), player)!.Value.Y;
            Assert.True(banner.Bounds.Height > 0 && audioY >= bannerY + banner.Bounds.Height);
            banner.IsVisible = false;
            Play(player); Play(player);
            Assert.False(banner.IsVisible);
            Play(player); // pause before loading another enhancement
            typeof(EnhancementPlayerWindow).GetMethod("UpdateHostUi", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(player, new object?[] { WebcamEnhancement(), null });
            player.PlayerKey(Key.Space);
            Assert.True(banner.IsVisible);
            banner.IsVisible = false;
            player.PlayerKey(Key.Space); // pause
            typeof(EnhancementPlayerWindow).GetMethod("UpdateHostUi", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(player, new object?[] { WebcamEnhancement(), null });
            typeof(EnhancementPlayerWindow).GetMethod("OnVideoWebMessage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(player, new object?[] { "ccp_key:Space:0:0" });
            Assert.True(banner.IsVisible);
            typeof(EnhancementPlayerWindow).GetMethod("UpdateHostUi", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(player, new object?[] { new Enhancement(), null });
            Tick(player);
            Assert.False(player.FindControl<Button>("BtnEyeTracking")!.IsVisible);
        }
        finally { player.Close(); EnhancementHostService.WebcamProvider = oldProvider; }
        return Task.CompletedTask;
    });
}
