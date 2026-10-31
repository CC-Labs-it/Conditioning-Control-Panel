using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Interactivity;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

public class CameraShortcutTests
{
    [Fact]
    public void ParsesSavedChordAndFallsBackToDefault()
    {
        var prompt = CoreSettings.Current.CompanionPrompt!;
        prompt.CameraShortcutKey = "F8";
        prompt.CameraShortcutModifiers = "ctrl+shift+win";
        Assert.Equal((Key.F8, KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Meta), MainShellWindow.CameraShortcut());
        Assert.Equal("Ctrl+Shift+Win+F8", MainShellWindow.FormatCameraShortcut());
        prompt.CameraShortcutKey = "bad-key";
        prompt.CameraShortcutModifiers = "bad-mod";
        Assert.Equal((Key.K, KeyModifiers.Control | KeyModifiers.Alt), MainShellWindow.CameraShortcut());
    }

    [Fact]
    public void RegistersReplacesDisablesAndReleasesGlobalChord() => AvaloniaTestDispatcher.Run(() =>
    {
        LabWebcamStatusTests.Setup();
        var prompt = CoreSettings.Current.CompanionPrompt!;
        prompt.CameraShortcutGlobal = true;
        prompt.CameraShortcutKey = "F8";
        prompt.CameraShortcutModifiers = "Control,Shift";
        int registrations = 0, releases = 0;
        var shell = new MainShellWindow();
        shell.RegisterCameraChord = (mods, key, _) =>
        {
            Assert.Equal(ChordMods.Ctrl | ChordMods.Shift, mods);
            Assert.Equal("F8", key);
            registrations++;
            return true;
        };
        shell.UnregisterCameraChord = () => releases++;
        try
        {
            shell.Show();
            Assert.Equal(1, registrations);
            shell.ApplyCameraShortcut();
            Assert.Equal(2, registrations);
            prompt.CameraShortcutGlobal = false;
            shell.ApplyCameraShortcut();
            Assert.Equal(2, registrations);
            Assert.Equal(3, releases);
        }
        finally { shell.RequestExit(); }
        Assert.Equal(4, releases);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LocalChordFallsBackAndDeclinedConsentNeverOpensCamera(bool global) => AvaloniaTestDispatcher.Run(() =>
    {
        LabWebcamStatusTests.Setup();
        var prompt = CoreSettings.Current.CompanionPrompt!;
        prompt.CameraShortcutGlobal = global;
        prompt.CameraShortcutKey = "K";
        prompt.CameraShortcutModifiers = "Control,Alt";
        CoreSettings.Current.WebcamConsentGiven = false;
        var shell = new MainShellWindow();
        shell.RegisterCameraChord = (_, _, _) => false;
        shell.UnregisterCameraChord = () => { };
        try
        {
            shell.Show();
            var wrong = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.J, KeyModifiers = KeyModifiers.Control | KeyModifiers.Alt };
            shell.RaiseEvent(wrong);
            Assert.False(wrong.Handled);
            shell.Hide();
            var press = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.K, KeyModifiers = KeyModifiers.Control | KeyModifiers.Alt };
            shell.RaiseEvent(press);
            Assert.True(press.Handled);
            Dispatcher.UIThread.RunJobs();
            Assert.True(shell.IsVisible);
            var consent = Assert.Single(shell.OwnedWindows.OfType<ConditioningControlPanel.Avalonia.Views.Dialogs.WebcamConsentDialog>());
            consent.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.False(ConditioningControlPanel.Avalonia.Platform.WebcamTracker.Instance.IsRunning);
        }
        finally { foreach (var w in shell.OwnedWindows.ToArray()) w.Close(); shell.RequestExit(); }
    });

    [Fact]
    public void BothSettingsSurfacesPaintTheSavedChord() => AvaloniaTestDispatcher.Run(() =>
    {
        LabWebcamStatusTests.Setup();
        CoreSettings.Current.CompanionPrompt!.CameraShortcutKey = "F8";
        CoreSettings.Current.CompanionPrompt!.CameraShortcutModifiers = "Control";
        var devices = new DevicesSettingsSection();
        Assert.Equal("Ctrl+F8", devices.FindControl<TextBlock>("TxtCameraShortcutLabelDevices")!.Text);
        var cell = new WorkshopBehaviorCell();
        var host = new Window();
        host.Content = cell;
        host.Show();
        Assert.Equal("Ctrl+F8", cell.FindControl<TextBlock>("TxtCameraShortcutLabel")!.Text);
        host.Close();
    });
    [Fact]
    public Task ShortcutRebindAndLocalFallbackRestoreConsentOwnerWithoutStartingCamera() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        LabWebcamStatusTests.Setup();
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
            var deadline = DateTime.UtcNow.AddSeconds(10); // P73: bound by time, not turns
            while (DateTime.UtcNow < deadline && !shell.OwnedWindows.OfType<WebcamConsentDialog>().Any())
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
