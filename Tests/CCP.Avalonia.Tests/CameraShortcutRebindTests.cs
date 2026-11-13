using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Restack b30 (slices of #3617): the camera shortcut pills report and rebind the saved
/// combo, as WPF MainWindow.SessionIO.cs:1381 RefreshCameraShortcutLabel / :1403 BtnCameraShortcut_Click.</summary>
public sealed class CameraShortcutRebindTests
{
    private sealed class Registration : IDisposable { public void Dispose() { } }

    [Fact]
    public void BothSettingsSurfacesPaintTheSavedChord() => AvaloniaTestDispatcher.Run(() =>
    {
        LabWebcamStatusTests.Setup();
        var p = CoreSettings.Current.CompanionPrompt!;
        var old = (p.CameraShortcutKey, p.CameraShortcutModifiers);
        var host = new Window();
        try
        {
            (p.CameraShortcutKey, p.CameraShortcutModifiers) = ("F8", "Control");
            var devices = new DevicesSettingsSection();
            Assert.Equal("Ctrl+F8", devices.FindControl<TextBlock>("TxtCameraShortcutLabelDevices")!.Text);
            var cell = new WorkshopBehaviorCell();
            host.Content = cell;
            host.Show();
            Assert.Equal("Ctrl+F8", cell.FindControl<TextBlock>("TxtCameraShortcutLabel")!.Text);
        }
        finally { host.Close(); (p.CameraShortcutKey, p.CameraShortcutModifiers) = old; }
    });

    [Fact]
    public Task ResetInTheCaptureDialogRestoresTheDefaultAndRearmsTheHotkey() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        LabWebcamStatusTests.Setup();
        var p = CoreSettings.Current.CompanionPrompt!;
        var old = (p.CameraShortcutKey, p.CameraShortcutModifiers, p.CameraShortcutGlobal);
        var shell = new MainShellWindow();
        (ChordMods Mods, string Key)? armed = null;
        shell.RegisterCameraHotkey = (_, m, k, _) => { armed = (m, k); return new Registration(); };
        shell.RegisterQuickRecal = (_, _) => new Registration();
        try
        {
            (p.CameraShortcutKey, p.CameraShortcutModifiers, p.CameraShortcutGlobal) = ("J", "Control,Windows", true);
            shell.Show();
            shell.QuickRecalPlatformSupported = true;
            shell.StartQuickRecalHotkey();
            Assert.Equal((ChordMods.Ctrl | ChordMods.Win, "J"), armed);
            var edit = shell.RebindCameraShortcutAsync();
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline && !shell.OwnedWindows.OfType<ChatShortcutCaptureDialog>().Any())
            { Dispatcher.UIThread.RunJobs(); await Task.Yield(); }
            var capture = Assert.Single(shell.OwnedWindows.OfType<ChatShortcutCaptureDialog>());
            capture.FindControl<Button>("BtnReset")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await edit;
            Assert.Equal("K", p.CameraShortcutKey);
            Assert.Equal("Control,Alt", p.CameraShortcutModifiers);
            Assert.Equal("Ctrl+Alt+K", MainShellWindow.CameraShortcutChord);
            Assert.Equal((ChordMods.Ctrl | ChordMods.Alt, "K"), armed);
        }
        finally
        {
            foreach (var w in shell.OwnedWindows.ToArray()) w.Close();
            shell.StopQuickRecalHotkey();
            shell.RequestExit();
            (p.CameraShortcutKey, p.CameraShortcutModifiers, p.CameraShortcutGlobal) = old;
        }
    });
}
