using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.Webcam;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class DevicesDiagnosticsTests
{
    [Fact]
    public async Task MouthCountsResetAndSubscriptionsEndWithTheSection()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var section = new DevicesSettingsSection();
            var window = new Window { Content = section };
            window.Show();
            var tracker = WebcamTracker.Instance;
            var log = section.FindControl<TextBlock>("TxtWebcamDebugLog")!;
            tracker.RaiseMouthForTest();
            tracker.RaiseMouthForTest();
            Assert.Contains("Mouth-open #2", log.Text);
            tracker.RaiseBlinkForTest();
            var oldFactory = WebcamTracker.SourceFactory;
            var settings = CoreSettings.Current;
            var oldConsent = (settings.WebcamConsentGiven, settings.WebcamConsentVersion);
            try
            {
                settings.WebcamConsentGiven = true;
                settings.WebcamConsentVersion = WebcamConsent.ConsentVersion;
                WebcamTracker.SourceFactory = () => throw new System.InvalidOperationException("test camera unavailable");
                var button = section.FindControl<Button>("BtnWebcamDebugStart")!;
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Assert.IsAssignableFrom<Task>(section.WebcamDebugStartTask).WaitAsync(TimeSpan.FromSeconds(60));
                Assert.False(tracker.IsStarting);
                Assert.True(button.IsEnabled);
            }
            finally
            {
                try
                {
                    // Never restore the real camera factory while a timed-out start can still reach it.
                    if (section.WebcamDebugStartTask is { IsCompleted: false } pending)
                    {
                        tracker.Stop();
                        await pending;
                    }
                }
                finally
                {
                    WebcamTracker.SourceFactory = oldFactory;
                    (settings.WebcamConsentGiven, settings.WebcamConsentVersion) = oldConsent;
                }
            }
            Assert.Equal("Face: — | Blinks: 0 | Gaze: —", section.FindControl<TextBlock>("TxtWebcamDebugCounters")!.Text);
            tracker.RaiseMouthForTest();
            Assert.EndsWith("Mouth-open #1", log.Text!.TrimEnd());
            var before = log.Text;
            window.Close();
            tracker.RaiseMouthForTest();
            Assert.Equal(before, log.Text);

        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartButtonWaitsForCompletionAndRecoversOnFailure(bool throws)
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var tracker = WebcamTracker.Instance;
            tracker.Stop();
            var settings = CoreSettings.Current;
            var oldConsent = (settings.WebcamConsentGiven, settings.WebcamConsentVersion);
            try
            {
                settings.WebcamConsentGiven = true;
                settings.WebcamConsentVersion = WebcamConsent.ConsentVersion;
                var section = new DevicesSettingsSection();
                var window = new Window { Content = section };
                var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                section.StartWebcamDebug = () => completion.Task;
                try
                {
                    window.Show();
                    var button = section.FindControl<Button>("BtnWebcamDebugStart")!;
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var task = Assert.IsAssignableFrom<Task>(section.WebcamDebugStartTask);
                    Assert.False(task.IsCompleted);
                    Assert.False(button.IsEnabled);
                    if (throws) completion.SetException(new InvalidOperationException("test start failed"));
                    else completion.SetResult(false);
                    await task.WaitAsync(TimeSpan.FromSeconds(10));
                    Assert.True(button.IsEnabled);
                }
                finally
                {
                    completion.TrySetResult(false);
                    if (section.WebcamDebugStartTask is { } task) await task;
                    window.Close();
                }
            }
            finally
            {
                (settings.WebcamConsentGiven, settings.WebcamConsentVersion) = oldConsent;
            }
        });
    }

    [Fact]
    public Task MouthDiagnosticsRestoresConsent() =>
        AssertConsentRestored(MouthCountsResetAndSubscriptionsEndWithTheSection, false, "prior-denied");

    [Theory]
    [InlineData(false, "prior-denied", false)]
    [InlineData(true, "prior-granted", true)]
    public Task StartDiagnosticsRestoresConsent(bool given, string version, bool throws) =>
        AssertConsentRestored(() => StartButtonWaitsForCompletionAndRecoversOnFailure(throws), given, version);

    private static Task AssertConsentRestored(Func<Task> exercise, bool given, string version) =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var settings = CoreSettings.Current;
            var previous = (settings.WebcamConsentGiven, settings.WebcamConsentVersion);
            try
            {
                settings.WebcamConsentGiven = given;
                settings.WebcamConsentVersion = version;
                await exercise();
                Assert.Multiple(
                    () => Assert.Equal(given, settings.WebcamConsentGiven),
                    () => Assert.Equal(version, settings.WebcamConsentVersion));
            }
            finally
            {
                (settings.WebcamConsentGiven, settings.WebcamConsentVersion) = previous;
            }
        });
}
