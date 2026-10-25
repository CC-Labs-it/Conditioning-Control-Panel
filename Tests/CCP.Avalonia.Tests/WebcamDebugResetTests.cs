using Avalonia.Controls;
using ConditioningControlPanel;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using Xunit;

namespace CCP.Avalonia.Tests;

public class WebcamDebugResetTests
{
    [Fact]
    public void MonitorSelectionSavesAndMissingDisplayFallbackDoesNotRewriteTheChoice() => AvaloniaTestDispatcher.Run(() =>
    {
        LabWebcamStatusTests.Setup();
        string saved = CoreSettings.Current.WebcamCalibrationScreen;
        CoreSettings.Current.WebcamCalibrationScreen = "disconnected-test-display";
        var section = new DevicesSettingsSection();
        var window = new Window { Content = section };
        try
        {
            window.Show();
            var picker = section.FindControl<ComboBox>("CmbWebcamMonitor")!;
            Assert.Equal("disconnected-test-display", CoreSettings.Current.WebcamCalibrationScreen);
            if (window.Screens is { } screens && screens.All.Count > 0)
            {
                Assert.Equal("Primary", ((ComboBoxItem)picker.SelectedItem!).Tag);
                WebcamScreens.Place(window);
                Assert.Equal((screens.Primary ?? screens.All[0]).Bounds.Position, window.Position);
            }
            picker.Items.Add(new ComboBoxItem { Content = "Synthetic display", Tag = "test-display" });
            picker.SelectedIndex = picker.Items.Count - 1;
            Assert.Equal("test-display", CoreSettings.Current.WebcamCalibrationScreen);
        }
        finally
        {
            window.Close();
            CoreSettings.Current.WebcamCalibrationScreen = saved;
            CoreSettings.SaveImmediate();
        }
    });

    [Fact]
    public void ResetClearsCountersAndGazeWithoutDeletingTheEventLog() => AvaloniaTestDispatcher.Run(() =>
    {
        LabWebcamStatusTests.Setup();
        var section = new DevicesSettingsSection();
        var window = new Window { Content = section };
        try
        {
            window.Show();
            WebcamTracker.Instance.RaiseBlinkForTest();
            var counters = section.FindControl<TextBlock>("TxtWebcamDebugCounters")!;
            var log = section.FindControl<TextBlock>("TxtWebcamDebugLog")!;
            Assert.Contains("Blinks: 1", counters.Text);
            section.ResetWebcamDebug();
            Assert.Equal("Face: — | Blinks: 0 | Gaze: —", counters.Text);
            Assert.Contains("Blink #1", log.Text);
        }
        finally { window.Close(); }
    });
}
