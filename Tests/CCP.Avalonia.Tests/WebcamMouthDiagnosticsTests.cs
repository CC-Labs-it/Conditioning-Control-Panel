using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Webcam;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class WebcamMouthDiagnosticsTests
{
    // Feed the production tracker event without acquiring a camera.
    private static void Emit(string name) =>
        ((Action?)typeof(WebcamTracker).GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(WebcamTracker.Instance))?.Invoke();

    [Fact]
    public void MouthEventsLogOnceWhileAttachedAndUnsubscribeOnDetach() => AvaloniaTestDispatcher.Run(() =>
    {
        var availability = CoreWebcam.IsAvailableProvider;
        CoreWebcam.IsAvailableProvider = () => true;
        var section = new DevicesSettingsSection();
        var log = section.FindControl<TextBlock>("TxtWebcamDebugLog")!;
        var host = new Window { Content = section };
        try
        {
            host.Show();
            Emit("OnMouthOpen");
            Assert.Contains(Loc.GetF("webcam_debug_mouth_open_fmt", 1), log.Text);
            var before = log.Text;
            host.Content = null;
            Emit("OnMouthOpen");
            Assert.Equal(before, log.Text);
            host.Content = section;
            Emit("OnMouthOpen");
            Assert.Contains(Loc.GetF("webcam_debug_mouth_open_fmt", 2), log.Text);
            Assert.DoesNotContain(Loc.GetF("webcam_debug_mouth_open_fmt", 3), log.Text);
        }
        finally { host.Close(); CoreWebcam.IsAvailableProvider = availability; }
    });

    [Fact]
    public Task StartingTrackingResetsDiagnosticSession() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var tracker = WebcamTracker.Instance;
        var models = WebcamTracker.ModelDir;
        var availability = CoreWebcam.IsAvailableProvider;
        CoreWebcam.IsAvailableProvider = () => true;
        var section = new DevicesSettingsSection();
        var host = new Window { Content = section };
        var button = section.FindControl<Button>("BtnWebcamDebugStart")!;
        try
        {
            host.Show();
            Emit("OnMouthOpen");
            Emit("OnBlink");
            Emit("OnFaceFound");
            ((Action<GazeSide>?)typeof(WebcamTracker).GetField("OnGazeSide",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tracker))?.Invoke(GazeSide.Left);
            CoreSettings.Current.WebcamConsentGiven = true;
            CoreSettings.Current.WebcamConsentVersion = WebcamConsent.ConsentVersion;
            // No models means this real Start cannot reach camera acquisition.
            WebcamTracker.ModelDir = System.IO.Path.Combine(TestUserDataProfile.Root, "absent-models");
            Assert.True(button.IsEnabled);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains("Blinks: 0", section.FindControl<TextBlock>("TxtWebcamDebugCounters")!.Text);
            Assert.Contains("Gaze: —", section.FindControl<TextBlock>("TxtWebcamDebugCounters")!.Text);
            Assert.Contains("Face: —", section.FindControl<TextBlock>("TxtWebcamDebugCounters")!.Text);
            var log = section.FindControl<TextBlock>("TxtWebcamDebugLog")!;
            log.Text = "";
            Emit("OnMouthOpen");
            Assert.Contains(Loc.GetF("webcam_debug_mouth_open_fmt", 1), log.Text);
            Assert.DoesNotContain(Loc.GetF("webcam_debug_mouth_open_fmt", 2), log.Text);
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!button.IsEnabled)
            {
                Assert.True(DateTime.UtcNow < deadline, "Start did not complete");
                await Task.Delay(10);
            }
        }
        finally
        {
            await tracker.StopAsync();
            WebcamTracker.ModelDir = models;
            CoreWebcam.IsAvailableProvider = availability;
            host.Close();
        }
    });
}
