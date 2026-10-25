using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The Devices tracking-monitor pick (WPF MainWindow.LabTab.cs:1442) over a fake screen list.</summary>
public sealed class TrackingMonitorPickTests
{
    private static void Boot()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public async Task DevicesMonitorPick_ListsPrimaryThenDisplays_KeepsAnAbsentPick_AndSavesAChoice()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Boot();
            var s = CoreSettings.Current;
            var old = s.WebcamCalibrationScreen;
            var oldSource = DevicesSettingsSection.MonitorSource;
            var oldAvail = CoreWebcam.IsAvailableProvider;
            Window? window = null;
            try
            {
                CoreWebcam.IsAvailableProvider = () => true;
                DevicesSettingsSection.MonitorSource = _ => new[] { ("DP-1", 1920, 1080), ("HDMI-1", 2560, 1440) };
                s.WebcamCalibrationScreen = "DP-9";   // unplugged
                var section = new DevicesSettingsSection();
                window = new Window { Content = section };
                window.Show();

                var cmb = section.FindControl<ComboBox>("CmbWebcamMonitor")!;
                Assert.True(cmb.IsEnabled);
                var fmt = Loc.Get("webcam_monitor_item_fmt");
                Assert.Equal(new[] { Loc.Get("webcam_monitor_primary"), string.Format(fmt, 1, "DP-1", 1920, 1080), string.Format(fmt, 2, "HDMI-1", 2560, 1440) },
                    cmb.Items.Cast<ComboBoxItem>().Select(i => i.Content!.ToString()).ToArray());
                Assert.Equal(0, cmb.SelectedIndex);             // shown as Primary...
                Assert.Equal("DP-9", s.WebcamCalibrationScreen); // ...without saving over the pick

                cmb.SelectedIndex = 2;                           // the user picks HDMI-1
                Assert.Equal("HDMI-1", s.WebcamCalibrationScreen);
                Assert.Contains($"Calibration monitor set to {string.Format(fmt, 2, "HDMI-1", 2560, 1440)}.",
                    section.FindControl<TextBlock>("TxtWebcamDebugLog")!.Text);
                window.Content = null;                           // section shown again: the saved pick is restored
                window.Content = section;
                Assert.Equal(2, cmb.SelectedIndex);
                cmb.SelectedIndex = 0;
                Assert.Equal("Primary", s.WebcamCalibrationScreen);
            }
            finally
            {
                window?.Close();
                DevicesSettingsSection.MonitorSource = oldSource;
                CoreWebcam.IsAvailableProvider = oldAvail;
                s.WebcamCalibrationScreen = old;
            }
            return Task.CompletedTask;
        });
    }
}
