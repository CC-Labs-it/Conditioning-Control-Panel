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
            try
            {
                CoreSettings.Current.WebcamConsentGiven = true;
                CoreSettings.Current.WebcamConsentVersion = WebcamConsent.ConsentVersion;
                WebcamTracker.SourceFactory = () => throw new System.InvalidOperationException("test camera unavailable");
                section.FindControl<Button>("BtnWebcamDebugStart")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                for (int i = 0; i < 200 && tracker.IsStarting; i++) await Task.Delay(10);
                Assert.False(tracker.IsStarting);
            }
            finally { WebcamTracker.SourceFactory = oldFactory; }
            Assert.Equal("Face: — | Blinks: 0 | Gaze: —", section.FindControl<TextBlock>("TxtWebcamDebugCounters")!.Text);
            tracker.RaiseMouthForTest();
            Assert.EndsWith("Mouth-open #1", log.Text!.TrimEnd());
            var before = log.Text;
            window.Close();
            tracker.RaiseMouthForTest();
            Assert.Equal(before, log.Text);

        });
    }
}
