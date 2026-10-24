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
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class BlinkTrainerShellCompletionTests
{
    [Theory]
    [InlineData("Primary", false, 1)]
    [InlineData("Primary", true, 0, 1)]
    [InlineData("SECOND", true, 1)]
    [InlineData("absent", true, 1)]
    [InlineData("", false, 1)]
    public void TrackingMonitorPrecedence(string pick, bool dual, params int[] expected)
        => Assert.Equal(expected, BlinkTrainerSession.ResolveTrackingScreens(new[] { "first", "second" }, pick, dual, 1));

    [Fact]
    public void NoScreensIsSafe() => Assert.Empty(BlinkTrainerSession.ResolveTrackingScreens(Array.Empty<string>(), "absent", true, -1));

    [Theory]
    [InlineData("bad-key", "", Key.K, KeyModifiers.Control | KeyModifiers.Alt)]
    [InlineData("J", "Control,Windows", Key.J, KeyModifiers.Control | KeyModifiers.Meta)]
    [InlineData("F9", "Shift+Alt", Key.F9, KeyModifiers.Shift | KeyModifiers.Alt)]
    public Task CameraShortcutUsesWpfDefaultsAndModifierNames(string key, string modifiers, Key expectedKey, KeyModifiers expectedMods)
        => AvaloniaTestDispatcher.RunAsync(() =>
        {
            var s = CoreSettings.Current.CompanionPrompt;
            var old = (s.CameraShortcutKey, s.CameraShortcutModifiers);
            try
            {
                s.CameraShortcutKey = key; s.CameraShortcutModifiers = modifiers;
                Assert.Equal((expectedKey, expectedMods), MainShellWindow.CameraShortcut());
            }
            finally { (s.CameraShortcutKey, s.CameraShortcutModifiers) = old; }
            return Task.CompletedTask;
        });

    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public Task DevicesMonitorPickerPersistsSelectionWithoutOverwritingMissingDisplay() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var s = CoreSettings.Current;
        var old = s.WebcamCalibrationScreen;
        var oldAvailable = CoreWebcam.IsAvailableProvider;
        try
        {
            CoreWebcam.IsAvailableProvider = () => true;
            s.WebcamCalibrationScreen = "disconnected";
            var section = new DevicesSettingsSection();
            section.PopulateWebcamMonitors();
            var combo = section.FindControl<ComboBox>("CmbWebcamMonitor")!;
            Assert.True(combo.IsEnabled);
            Assert.Equal("Primary", ((ComboBoxItem)combo.SelectedItem!).Tag);
            Assert.Equal("disconnected", s.WebcamCalibrationScreen);
            var item = new ComboBoxItem { Content = "Test display", Tag = "test-display" };
            combo.Items.Add(item);
            combo.SelectedItem = item;
            Assert.Equal("test-display", s.WebcamCalibrationScreen);
        }
        finally { CoreWebcam.IsAvailableProvider = oldAvailable; s.WebcamCalibrationScreen = old; CoreSettings.Save(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ShortcutRebindAndLocalFallbackRestoreConsentOwnerWithoutStartingCamera() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var s = CoreSettings.Current;
        var prompt = s.CompanionPrompt;
        var old = (prompt.CameraShortcutKey, prompt.CameraShortcutModifiers, prompt.CameraShortcutGlobal, s.WebcamConsentGiven, s.WebcamConsentVersion);
        MainShellWindow? shell = null;
        int arms = 0, disarms = 0;
        try
        {
            prompt.CameraShortcutKey = "J";
            prompt.CameraShortcutModifiers = "Control,Windows";
            prompt.CameraShortcutGlobal = true;
            s.WebcamConsentGiven = false;
            s.WebcamConsentVersion = "";
            shell = new MainShellWindow
            {
                RegisterCameraChord = (mods, key, callback) =>
                {
                    arms++;
                    Assert.Equal(ChordMods.Ctrl | ChordMods.Win, mods);
                    Assert.Equal("J", key);
                    return false; // refused global grab retains in-window fallback
                },
                UnregisterCameraChord = () => disarms++
            };
            shell.Show();
            Assert.True(arms > 0);
            Assert.Equal("Ctrl+Win+J", MainShellWindow.FormatCameraShortcut());
            prompt.CameraShortcutGlobal = false;
            shell.ApplyCameraShortcut();
            int before = arms;
            shell.ApplyCameraShortcut();
            Assert.Equal(before, arms);
            Assert.True(disarms >= 2);
            var edit = shell.RebindCameraShortcut();
            var capture = Assert.Single(shell.OwnedWindows.OfType<ChatShortcutCaptureDialog>());
            capture.FindControl<Button>("BtnReset")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await edit;
            Assert.Equal("K", prompt.CameraShortcutKey);
            Assert.Equal("Control,Alt", prompt.CameraShortcutModifiers);
            prompt.CameraShortcutKey = "J";
            prompt.CameraShortcutModifiers = "Control,Windows";
            shell.ApplyCameraShortcut();
            shell.Hide();
            var keyEvent = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.J, KeyModifiers = KeyModifiers.Control | KeyModifiers.Meta };
            shell.RaiseEvent(keyEvent);
            Assert.True(keyEvent.Handled);
            for (int i = 0; i < 100 && !shell.OwnedWindows.OfType<WebcamConsentDialog>().Any(); i++)
            { Dispatcher.UIThread.RunJobs(); await Task.Yield(); }
            Assert.True(shell.IsVisible);
            var dialog = Assert.Single(shell.OwnedWindows.OfType<WebcamConsentDialog>());
            dialog.Close();
            Dispatcher.UIThread.RunJobs();
            await Task.Yield();
            Assert.False(ConditioningControlPanel.Avalonia.Platform.WebcamTracker.Instance.IsRunning);
        }
        finally
        {
            foreach (var dialog in shell?.OwnedWindows.ToArray() ?? Array.Empty<Window>()) dialog.Close();
            shell?.RequestExit();
            (prompt.CameraShortcutKey, prompt.CameraShortcutModifiers, prompt.CameraShortcutGlobal, s.WebcamConsentGiven, s.WebcamConsentVersion) = old;
        }
    });
}
