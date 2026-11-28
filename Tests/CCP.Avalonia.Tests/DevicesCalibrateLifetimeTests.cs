using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Services.Webcam;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class DevicesCalibrateLifetimeTests
{
    [Theory]
    [InlineData(false, null, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, null, true)]
    public async Task CalibrationPreservesCameraOwnership(bool initiallyRunning, bool? result, bool expectedRunning)
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var settings = CoreSettings.Current;
            var oldConsent = (settings.WebcamConsentGiven, settings.WebcamConsentVersion);
            var running = initiallyRunning;
            var stops = 0;
            var section = new DevicesSettingsSection();
            var window = new Window { Content = section };
            try
            {
                settings.WebcamConsentGiven = true;
                settings.WebcamConsentVersion = WebcamConsent.ConsentVersion;
                section.IsWebcamRunning = () => running;
                section.StartWebcamDebug = () => { running = true; return Task.FromResult(true); };
                section.StopWebcamDebug = () => { stops++; running = false; return Task.CompletedTask; };
                section.ShowCalibration = _ => Task.FromResult(result);
                window.Show();
                section.FindControl<Button>("BtnWebcamDebugCalibrate")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Assert.IsAssignableFrom<Task>(section.WebcamCalibrateTask).WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(expectedRunning, running);
                Assert.Equal(!initiallyRunning && result != true ? 1 : 0, stops);
                if (!expectedRunning)
                    Assert.Equal("Start tracking", section.FindControl<Button>("BtnWebcamDebugStart")!.Content);
            }
            finally
            {
                window.Close();
                (settings.WebcamConsentGiven, settings.WebcamConsentVersion) = oldConsent;
            }
        });
    }
}
