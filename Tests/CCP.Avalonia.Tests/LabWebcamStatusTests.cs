using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class LabWebcamStatusTests
{
    [Fact]
    public void PlayEyeCardsInitiallyShowTheStoppedTrackerHint() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var play = new PlayTabView();
        Assert.Equal(0.62, play.FindControl<Control>("PlayGazeCard")!.Opacity);
        Assert.Equal(0.62, play.FindControl<Control>("PlayFocusCard")!.Opacity);
        Assert.True(play.FindControl<Control>("PlayGazeNeedsTracker")!.IsVisible);
        Assert.True(play.FindControl<Control>("PlayFocusNeedsTracker")!.IsVisible);
    });

    [Fact]
    public void AllFourChipsFollowPublishedStatesAndDetachUnsubscribes() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var tracker = WebcamTracker.Instance;
        Assert.False(tracker.IsRunning); // no device is opened by this test
        var oldState = tracker.State;
        var play = new PlayTabView();
        var devices = new DevicesSettingsSection();
        var deeper = new DeeperTabView();
        var blink = new BlinkTrainerTabView { IsVisible = false }; // no preview/demo playback
        Control[] pages = { play, devices, deeper, blink };
        string[] names = { "TxtWebcamStatusChipPlay", "TxtWebcamDebugStatus", "TxtWebcamStatusChipDeeper", "TxtWebcamStatusChipBlinkTrainer" };
        var host = new Window { Content = new StackPanel { Children = { play, devices, deeper, blink } } };
        try
        {
            host.Show();
            foreach (var state in new[] { WebcamTrackingState.Starting, WebcamTrackingState.Error, WebcamTrackingState.Stopped })
            {
                tracker.SetTrackingState(state);
                Dispatcher.UIThread.RunJobs();
                for (int i = 0; i < pages.Length; i++)
                    Assert.True(Loc.Get(WebcamStatus.TextKey(state)) == pages[i].FindControl<TextBlock>(names[i])!.Text,
                        names[i] + " did not follow " + state + ": " + pages[i].FindControl<TextBlock>(names[i])!.Text);
            }
            host.Content = null;
            tracker.SetTrackingState(WebcamTrackingState.Error);
            Dispatcher.UIThread.RunJobs();
            for (int i = 0; i < pages.Length; i++)
                Assert.Equal(Loc.Get("rf_webcam_stopped"), pages[i].FindControl<TextBlock>(names[i])!.Text);
            // A late processing frame cannot relight capture UI after the camera stopped.
            tracker.SetTrackingState(WebcamTrackingState.Tracking);
            Assert.Equal(WebcamTrackingState.Error, tracker.State);
        }
        finally { host.Close(); tracker.SetTrackingState(oldState); Dispatcher.UIThread.RunJobs(); }
    });

    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
}
