using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class WebcamMonitorSettingsTests
{
    [Theory]
    [InlineData("Primary", 1)]
    [InlineData("primary", 1)]
    [InlineData("gone", 1)]
    [InlineData("left", 0)]
    public void ResolveSavedMonitorOrPrimary(string saved, int expected)
    {
        Assert.Equal(expected, WebcamScreen.Pick(new[] { ("Left", false), ("Right", true) }, saved));
        Assert.Equal(-1, WebcamScreen.Pick(System.Array.Empty<(string, bool)>(), saved));
    }

    [Fact]
    public async Task MissingMonitorDisplaysPrimaryWithoutOverwritingSavedChoice()
    {
        var old = CoreSettings.Current.WebcamCalibrationScreen;
        try
        {
            CoreSettings.Current.WebcamCalibrationScreen = "Disconnected display";
            await AvaloniaTestDispatcher.RunAsync(() =>
            {
                if (Application.Current is null)
                    AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                        .SetupWithoutStarting();
                var section = new DevicesSettingsSection();
                section.RefreshWebcamMonitorList();
                var combo = section.FindControl<ComboBox>("CmbWebcamMonitor")!;
                Assert.Equal("Primary", ((ComboBoxItem)combo.SelectedItem!).Tag);
                Assert.Equal("Disconnected display", CoreSettings.Current.WebcamCalibrationScreen);
                combo.Items.Add(new ComboBoxItem { Content = "Other display", Tag = "Other" });
                combo.SelectedIndex = combo.Items.Count - 1;
                Assert.Equal("Other", CoreSettings.Current.WebcamCalibrationScreen);
                Assert.Contains("Calibration monitor set to Other display.", section.FindControl<TextBlock>("TxtWebcamDebugLog")!.Text);
                var window = new Window { Content = section };
                window.Show();
                WebcamTracker.Instance.RaiseBlinkForTest();
                WebcamTracker.Instance.RaiseGazeSideForTest(GazeSide.Left);
                WebcamTracker.Instance.RaiseGazeSideForTest(GazeSide.Left);
                var log = section.FindControl<TextBlock>("TxtWebcamDebugLog")!;
                Assert.Contains("Blink #1", log.Text);
                Assert.Equal(2, log.Text!.Split("Gaze → Left").Length);
                section.ResetDebugCounters();
                Assert.Equal("Face: — | Blinks: 0 | Gaze: —", section.FindControl<TextBlock>("TxtWebcamDebugCounters")!.Text);
                var before = log.Text;
                window.Close();
                WebcamTracker.Instance.RaiseBlinkForTest();
                Assert.Equal(before, log.Text);
                return Task.CompletedTask;
            });
        }
        finally { CoreSettings.Current.WebcamCalibrationScreen = old; CoreSettings.SaveImmediate(); }
    }
}
