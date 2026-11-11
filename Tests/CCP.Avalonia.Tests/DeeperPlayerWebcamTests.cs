using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
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
        public bool Open() { Opens++; return OpenResult; }
        public bool Read(Mat frame) { frame.Create(480, 640, MatType.CV_8UC3); frame.SetTo(Scalar.All(0)); System.Threading.Thread.Sleep(10); return true; }
        public void Dispose() { }
    }
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Tick(EnhancementPlayerWindow w) => typeof(EnhancementPlayerWindow).GetMethod("WebcamTick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, new object?[] { null, EventArgs.Empty });
    private static Enhancement WebcamEnhancement() => new() { Metadata = new EnhancementMetadata { AutoTags = { EnhancementAutoTagger.TagWebcam } } };
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
        var old = (settings.WebcamConsentGiven, settings.WebcamConsentVersion, WebcamTracker.SourceFactory);
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
            Click(button);
            await Task.Delay(100);
            var message = Assert.Single(player.OwnedWindows.OfType<MessageDialog>());
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
            (settings.WebcamConsentGiven, settings.WebcamConsentVersion, WebcamTracker.SourceFactory) = old;
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
        var old = (settings.WebcamConsentGiven, settings.WebcamConsentVersion, WebcamTracker.SourceFactory);
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
            await Until(() => Equals(player.FindControl<Button>("BtnEyeTracking")!.Content, ConditioningControlPanel.Localization.Loc.Get("deeper_player_btn_eye_tracking_stop")));
            player.Close();
            await Until(() => !WebcamTracker.Instance.IsRunning);
            Assert.True(await WebcamTracker.Instance.StartAsync());
            var second = new EnhancementPlayerWindow(WebcamEnhancement(), null);
            try
            {
                second.Show();
                typeof(EnhancementPlayerWindow).GetField("_isPlaying", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(second, true);
                Tick(second);
                Assert.False(second.FindControl<Border>("WebcamBanner")!.IsVisible);
            }
            finally { second.Close(); }
            await Task.Delay(300);
            Assert.True(WebcamTracker.Instance.IsRunning);
        }
        finally
        {
            player.Close();
            await WebcamTracker.Instance.StopAsync();
            (settings.WebcamConsentGiven, settings.WebcamConsentVersion, WebcamTracker.SourceFactory) = old;
        }
    });
    [Fact]
    public Task FailedStartReportsError() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var settings = CoreSettings.Current;
        var old = (settings.WebcamConsentGiven, settings.WebcamConsentVersion, WebcamTracker.SourceFactory);
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
            (settings.WebcamConsentGiven, settings.WebcamConsentVersion, WebcamTracker.SourceFactory) = old;
        }
    });
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
    public Task BannerIsOfferedOnlyOnceAndButtonRequiresWebcam() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var player = new EnhancementPlayerWindow(WebcamEnhancement(), null);
        try
        {
            player.Show();
            typeof(EnhancementPlayerWindow).GetField("_isPlaying", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(player, true);
            Tick(player);
            var banner = player.FindControl<Border>("WebcamBanner")!;
            Assert.True(banner.IsVisible);
            banner.IsVisible = false;
            Tick(player);
            Assert.False(banner.IsVisible);
            typeof(EnhancementPlayerWindow).GetMethod("UpdateHostUi", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(player, new object?[] { WebcamEnhancement(), null });
            Tick(player);
            Assert.True(banner.IsVisible);
            typeof(EnhancementPlayerWindow).GetMethod("UpdateHostUi", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(player, new object?[] { new Enhancement(), null });
            Tick(player);
            Assert.False(player.FindControl<Button>("BtnEyeTracking")!.IsVisible);
        }
        finally { player.Close(); }
        return Task.CompletedTask;
    });
}
