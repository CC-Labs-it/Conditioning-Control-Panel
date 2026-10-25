using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Speech;
using ConditioningControlPanel.Services.Webcam;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class DevicesSettingsFinishTests
{
    private static Task WithSection(Func<MainShellWindow, DevicesSettingsSection, AppSettings, Task> test) =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current == null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var oldService = CoreSettings.ServiceProvider;
            var oldPremium = CoreEntitlement.HasPremiumProvider;
            var oldFree = CoreEntitlement.IsFreeTodayProvider;
            var oldSpeech = CoreSpeech.IsAvailableProvider;
            var oldDevices = CoreSpeech.EnumerateInputDevicesProvider;
            var oldWebcam = CoreWebcam.IsAvailableProvider;
            var oldPtt = X11PanicKey.PushToTalk;
            var oldPttKey = X11PanicKey.PushToTalkKey;
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            CoreEntitlement.HasPremiumProvider = () => true;
            CoreEntitlement.IsFreeTodayProvider = _ => false;
            CoreSpeech.IsAvailableProvider = () => true;
            CoreWebcam.IsAvailableProvider = () => true;
            CoreSpeech.EnumerateInputDevicesProvider = () => new[] { new SpeechInputDevice(-1, "System default"), new SpeechInputDevice(0, "Test mic") };
            MainShellWindow? shell = null;
            try
            {
                shell = new MainShellWindow();
                shell.Show();
                shell.ShowTab("appsettings");
                Dispatcher.UIThread.RunJobs();   // the page attaches on a posted job
                var section = shell.AppSettingsPage!.FindControl<DevicesSettingsSection>("SectionDevices")!;
                await test(shell, section, service.Current);
            }
            finally
            {
                GazeDebugCursor.CloseActive();
                shell?.Close();
                service.SealForReset();
                CoreSettings.ServiceProvider = oldService;
                CoreEntitlement.HasPremiumProvider = oldPremium;
                CoreEntitlement.IsFreeTodayProvider = oldFree;
                CoreSpeech.IsAvailableProvider = oldSpeech;
                CoreSpeech.EnumerateInputDevicesProvider = oldDevices;
                CoreWebcam.IsAvailableProvider = oldWebcam;
                X11PanicKey.PushToTalk = oldPtt;
                X11PanicKey.PushToTalkKey = oldPttKey;
            }
        });

    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Key(Window w, Key key) => w.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
    private static void Pump() => Dispatcher.UIThread.RunJobs();

    [Fact]
    public Task VoiceGatesConsentWarningAndUngatedDisarm() => WithSection(async (shell, section, s) =>
    {
        var wake = section.FindControl<CheckBox>("ChkSpeechWakeWord")!;
        var ptt = section.FindControl<CheckBox>("ChkSpeechPushToTalk")!;
        CoreEntitlement.HasPremiumProvider = () => false;
        ptt.IsChecked = true; Pump();
        Assert.False(s.SpeechPushToTalkEnabled);
        Assert.False(ptt.IsChecked);
        Assert.Empty(shell.OwnedWindows.OfType<MicConsentDialog>());

        CoreEntitlement.HasPremiumProvider = () => true;
        ptt.IsChecked = true;
        var consent = Assert.Single(shell.OwnedWindows.OfType<MicConsentDialog>());
        Click(consent.FindControl<Button>("BtnCancel")!);
        await Task.Yield(); Pump();
        Assert.False(s.MicConsentGiven || s.SpeechPushToTalkEnabled);
        Assert.False(ptt.IsChecked);

        s.MicConsentGiven = true;
        wake.IsChecked = true;
        var warning = Assert.Single(shell.OwnedWindows.OfType<MessageDialog>());
        Assert.Equal("Always-on microphone", warning.Title);
        Assert.Contains("Turn on always-on listening?", warning.FindControl<TextBlock>("TxtMessage")!.Text);
        Click(warning.FindControl<Button>("BtnCancel")!);
        await Task.Yield(); Pump();
        Assert.False(s.SpeechWakeWordEnabled);
        wake.IsChecked = true;
        Click(Assert.Single(shell.OwnedWindows.OfType<MessageDialog>()).FindControl<Button>("BtnOk")!);
        await Task.Yield(); Pump();
        Assert.True(s.SpeechWakeWordEnabled);
        Assert.True(shell.WakeLoopArmed);
        ptt.IsChecked = true; Pump();
        Assert.True(s.SpeechPushToTalkEnabled && shell.PushToTalkArmed);
        CoreEntitlement.HasPremiumProvider = () => false;
        wake.IsChecked = false; Pump();
        ptt.IsChecked = false; Pump();
        Assert.False(s.SpeechWakeWordEnabled || s.SpeechPushToTalkEnabled);
        Assert.False(shell.WakeLoopArmed || shell.PushToTalkArmed);
    });

    [Fact]
    public Task PttCaptureIgnoresModifiersRestoresLocalizationAndCancelsOnDetach() => WithSection((shell, section, s) =>
    {
        var button = section.FindControl<Button>("BtnSetPttKey")!;
        s.SpeechPushToTalkKey = "F8";
        Click(button); Click(button);
        Assert.True(DevicesSettingsSection.CapturingPttKey);
        var surfaces = PanicSurfaces.All;
        bool stopped = false;
        PanicSurfaces.All = new[] { new PanicSurfaces.Surface("probe", _ => stopped = true) };
        try { shell.HandlePanicKeyPress(DateTime.Now); Assert.True(stopped); } // WPF hook has no PTT gate: panic still fires
        finally { PanicSurfaces.All = surfaces; }
        Action? release = null;
        var later = DevicesSettingsSection.PttReleaseLater;
        DevicesSettingsSection.PttReleaseLater = (a, _) => release = a;
        try
        {
        Key(shell, global::Avalonia.Input.Key.LeftCtrl);
        Assert.Equal("F8", s.SpeechPushToTalkKey);
        Key(shell, global::Avalonia.Input.Key.Escape);   // WPF PTT binds Escape; only Pause clears it.
        Assert.Equal("Escape", s.SpeechPushToTalkKey);
        Assert.True(DevicesSettingsSection.CapturingPttKey); // queued hook copy remains suppressed
        Assert.NotNull(release);
        release!(); // step the 300 ms one-shot
        Assert.False(DevicesSettingsSection.CapturingPttKey);
        }
        finally { DevicesSettingsSection.PttReleaseLater = later; }
        var language = LocalizationManager.Instance.CurrentLanguage;
        try
        {
            LocalizationManager.Instance.SetLanguage("ja"); Pump();
            Assert.Equal(Loc.Get("set2_btn_set_key"), Assert.IsType<TextBlock>(button.Content).Text);
        }
        finally { LocalizationManager.Instance.SetLanguage(language); }
        Click(button);
        shell.Content = null; // detaching the section cancels the capture
        Assert.False(DevicesSettingsSection.CapturingPttKey);
        return Task.CompletedTask;
    });

    [Fact]
    public Task MicHeadphonesAndWakeWordsRequoteTheListeningChips() => WithSection((shell, section, s) =>
    {
        var tab = shell.SheListeningPage!;
        s.MicConsentGiven = true;
        s.SpeechWakeWordEnabled = true;
        section.FindControl<ComboBox>("CmbMicDevice")!.SelectedIndex = 1;
        Assert.Equal("Test mic", s.SpeechInputDeviceName);
        Assert.Equal("Test mic", tab.TxtSL_MicDeviceChip.Text);
        Assert.True(shell.WakeLoopArmed); // picking a mic reconciles the armed voice host immediately
        section.FindControl<CheckBox>("ChkHeadphones")!.IsChecked = true;
        Assert.Equal(Loc.Get("set2_chip_on"), tab.TxtSL_HeadphonesChip.Text);
        var words = section.FindControl<TextBox>("TxtSpeechWakeWords")!;
        words.Text = "  hello test  ";
        words.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
        Assert.Equal("hello test", s.SpeechWakeWords);
        Assert.Equal("hello test", tab.TxtSL_WakeWordChip.Text);
        words.Text = " "; words.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
        Assert.Equal("hey bambi", words.Text);
        return Task.CompletedTask;
    });

    [Fact]
    public Task CursorToggleFaceLossAndPanicHaveLiveCallers() => WithSection((shell, section, s) =>
    {
        var make = GazeDebugCursor.MakeClickThrough;
        var calibration = WebcamTracker.Instance.Calibration;
        try
        {
            GazeDebugCursor.MakeClickThrough = _ => true; // headless windows only
            var toggle = section.FindControl<CheckBox>("ChkWebcamDebugCursor")!;
            toggle.IsChecked = true;
            var cursor = Assert.IsType<GazeDebugCursor>(GazeDebugCursor.Active);
            WebcamTracker.Instance.Calibration = new WebcamCalibrationData { MonitorBounds = new MonitorBoundsRecord { DpiScale = 1 } };
            WebcamTracker.Instance.RaiseGazeMoveForTest(new Point(30, 40));
            Assert.True(cursor.HasGaze);
            WebcamTracker.Instance.RaiseFaceForTest(false);
            Assert.False(cursor.HasGaze);
            WebcamTracker.Instance.RaiseFaceForTest(true);
            WebcamTracker.Instance.RaiseGazeMoveForTest(new Point(40, 50));
            Assert.True(cursor.HasGaze);
            Assert.False(cursor.ShowActivated || cursor.IsHitTestVisible);
            Assert.True(cursor.Topmost);
            PanicSurfaces.All.Single(p => p.Id == "gaze-debug-cursor").Stop(shell);
            Assert.Null(GazeDebugCursor.Active);
            Assert.False(toggle.IsChecked);
            toggle.IsChecked = false;
            Assert.Contains("Debug cursor hidden.", section.FindControl<TextBlock>("TxtWebcamDebugLog")!.Text);
            GazeDebugCursor.MakeClickThrough = _ => false;
            toggle.IsChecked = true;
            Assert.Null(GazeDebugCursor.Active); // unsupported transparency/click-through never blocks desktop
            Assert.False(toggle.IsChecked);
            Assert.Contains("Debug cursor unavailable", section.FindControl<TextBlock>("TxtWebcamDebugLog")!.Text);
            GazeDebugCursor.MakeClickThrough = _ => true;
            toggle.IsChecked = true;
            Assert.NotNull(GazeDebugCursor.Active);
            shell.Close();
            Assert.Null(GazeDebugCursor.Active);
        }
        finally { GazeDebugCursor.CloseActive(); GazeDebugCursor.MakeClickThrough = make; WebcamTracker.Instance.Calibration = calibration; }
        return Task.CompletedTask;
    });
}
