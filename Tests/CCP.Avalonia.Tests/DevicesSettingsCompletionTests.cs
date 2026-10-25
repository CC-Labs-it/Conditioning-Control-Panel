using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class DevicesSettingsCompletionTests
{
    [Fact]
    public Task PttCaptureIgnoresModifiersAndPreservesTheLocalizedCaption() => Run((section, window) =>
    {
        var button = section.FindControl<Button>("BtnSetPttKey")!;
        var caption = button.Content;
        CoreSettings.Current.SpeechPushToTalkKey = "F8";
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.LeftCtrl });
        Assert.Equal("F8", CoreSettings.Current.SpeechPushToTalkKey);
        Assert.NotSame(caption, button.Content);
        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F10 });
        Assert.Equal("F10", CoreSettings.Current.SpeechPushToTalkKey);
        Assert.Equal("F10", section.FindControl<TextBlock>("TxtPttKey")!.Text);
        Assert.Same(caption, button.Content);
    });

    [Fact]
    public Task TurningModesOffAlwaysPersistsWithoutAnOwnerOrConsent() => Run((section, _) =>
    {
        var s = CoreSettings.Current;
        s.MicConsentGiven = true;
        s.SpeechWakeWordEnabled = s.SpeechPushToTalkEnabled = true;
        section.SyncFromSettings();
        section.FindControl<CheckBox>("ChkSpeechWakeWord")!.IsChecked = false;
        section.FindControl<CheckBox>("ChkSpeechPushToTalk")!.IsChecked = false;
        Assert.False(s.SpeechWakeWordEnabled);
        Assert.False(s.SpeechPushToTalkEnabled);
    });

    [Fact]
    public Task EnablingWithoutAShellRevertsWithoutOpeningTheMic() => Run((section, _) =>
    {
        CoreSettings.Current.SpeechWakeWordEnabled = CoreSettings.Current.SpeechPushToTalkEnabled = false;
        section.SyncFromSettings();
        var toggle = section.FindControl<CheckBox>("ChkSpeechPushToTalk")!;
        toggle.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.False(toggle.IsChecked);
        Assert.False(CoreSettings.Current.SpeechPushToTalkEnabled);
    });

    [Fact]
    public Task MonitorRefreshFallsBackWithoutOverwritingTheSavedChoiceAndSelectionLogs() => Run((section, window) =>
    {
        CoreSettings.Current.WebcamCalibrationScreen = "disconnected";
        section.OnSectionShown();
        var combo = section.FindControl<ComboBox>("CmbWebcamMonitor")!;
        Assert.True(combo.IsEnabled);
        Assert.Equal("Primary", ((ComboBoxItem)combo.SelectedItem!).Tag);
        Assert.Equal("disconnected", CoreSettings.Current.WebcamCalibrationScreen);
        var item = new ComboBoxItem { Content = "Test monitor", Tag = "test-display" };
        combo.Items.Add(item);
        combo.SelectedItem = item;
        Assert.Equal("test-display", CoreSettings.Current.WebcamCalibrationScreen);
        Assert.Contains("Calibration monitor set to Test monitor.", section.FindControl<TextBlock>("TxtWebcamDebugLog")!.Text);
        CoreSettings.Current.WebcamCalibrationScreen = "Primary";
        WebcamCalibrationWindow.PlaceOnCalibratedScreen(window);
        Assert.Equal(window.Screens.Primary!.Bounds.Position, window.Position);
    });

    [Fact]
    public Task CameraShortcutMatchesExactModifiersAndHonorsGlobalOptOut() => Run((_, _) =>
    {
        var settings = CoreSettings.Current.CompanionPrompt!;
        settings.CameraShortcutKey = "F10";
        settings.CameraShortcutModifiers = "Control,Windows";
        settings.CameraShortcutGlobal = true;
        int calls = 0;
        var old = CameraShortcut.Pressed;
        CameraShortcut.Pressed = () => calls++;
        CameraShortcut.Release();
        try
        {
            CameraShortcut.Route(Key.F10, KeyModifiers.Control);
            Assert.Equal(0, calls);
            CameraShortcut.Route(Key.F10, KeyModifiers.Control | KeyModifiers.Meta);
            Assert.Equal(1, calls);
            CameraShortcut.Route(Key.F10, KeyModifiers.Control | KeyModifiers.Meta);
            Assert.Equal(1, calls); // MOD_NOREPEAT on WPF
            CameraShortcut.Release();
            settings.CameraShortcutGlobal = false;
            CameraShortcut.Route(Key.F10, KeyModifiers.Control | KeyModifiers.Meta);
            Assert.Equal(1, calls);
            Assert.True(CameraShortcut.Matches(Key.F10, KeyModifiers.Control | KeyModifiers.Meta));
        }
        finally { CameraShortcut.Release(); CameraShortcut.Pressed = old; }
    });

    [Fact]
    public Task DebugCursorClosesOnPanicAndRefusesAnOpaqueDesktop() => Run((section, _) =>
    {
        var old = GazeDebugCursorWindow.MakeClickThrough;
        try
        {
            GazeDebugCursorWindow.MakeClickThrough = _ => true;
            var toggle = section.FindControl<CheckBox>("ChkWebcamDebugCursor")!;
            toggle.IsChecked = true;
            Assert.NotNull(GazeDebugCursorWindow.Active);
            Assert.False(GazeDebugCursorWindow.Active!.DotVisible); // no running calibrated tracker
            Assert.Contains(PanicSurfaces.All, surface => surface.Id == "gaze-debug-cursor");
            foreach (var surface in PanicSurfaces.All)
                if (surface.Id == "gaze-debug-cursor") surface.Stop(null);
            Assert.Null(GazeDebugCursorWindow.Active);
            toggle.IsChecked = false;
            GazeDebugCursorWindow.MakeClickThrough = _ => false;
            toggle.IsChecked = true;
            Assert.False(toggle.IsChecked);
            Assert.Null(GazeDebugCursorWindow.Active);
        }
        finally { GazeDebugCursorWindow.CloseActive(); GazeDebugCursorWindow.MakeClickThrough = old; }
    });

    private static Task Run(Action<DevicesSettingsSection, Window> action) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current == null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var old = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        Window? window = null;
        try
        {
            var section = new DevicesSettingsSection();
            window = new Window { Content = section };
            window.Show();
            action(section, window);
        }
        finally
        {
            window?.Close();
            service.SealForReset();
            CoreSettings.ServiceProvider = old;
        }
        return Task.CompletedTask;
    });
}
