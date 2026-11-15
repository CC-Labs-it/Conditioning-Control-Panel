using System;
using System.IO;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public class LaunchSettingsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FirstOpenStartsOnlyWhenEnabled(bool enabled) => AvaloniaTestDispatcher.Run(() =>
    {
        var settings = CoreSettings.Current;
        var oldAuto = settings.AutoStartEngine;
        var oldRun = settings.RunOnStartup;
        var oldAudio = settings.AudioOnlySession;
        var oldHook = CoreEngine.StartedHook;
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        XdgAutostart.DirectoryOverride = dir;
        WindowsStartupShortcut.DirectoryOverride = dir;
        int starts = 0;
        CoreEngine.Stop();
        CoreEngine.StartedHook = () => starts++;
        settings.AutoStartEngine = enabled;
        settings.RunOnStartup = false;
        settings.AudioOnlySession = true;
        var shell = new MainShellWindow();
        try
        {
            shell.ApplyLaunchSettings();
            shell.Show();
            Assert.Equal(enabled, CoreEngine.IsRunning);
            Assert.Equal(enabled ? 1 : 0, starts);
            CoreEngine.Stop();
            shell.Hide();
            shell.Show();
            Assert.False(CoreEngine.IsRunning);
            Assert.Equal(enabled ? 1 : 0, starts);
        }
        finally
        {
            CoreEngine.Stop(); shell.Close(); CoreEngine.StartedHook = oldHook;
            settings.AutoStartEngine = oldAuto; settings.RunOnStartup = oldRun; settings.AudioOnlySession = oldAudio;
            XdgAutostart.DirectoryOverride = null; WindowsStartupShortcut.DirectoryOverride = null;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LaunchReconcilesStartupEntry(bool enabled) => AvaloniaTestDispatcher.Run(() =>
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        XdgAutostart.DirectoryOverride = dir;
        WindowsStartupShortcut.DirectoryOverride = dir;
        var settings = CoreSettings.Current;
        var old = settings.RunOnStartup;
        var shell = new MainShellWindow();
        try
        {
            Assert.True(SessionAutostart.SetStartupState(!enabled));
            settings.RunOnStartup = enabled;
            shell.ApplyLaunchSettings();
            Assert.Equal(enabled, SessionAutostart.IsRegistered());
        }
        finally
        {
            shell.Close(); settings.RunOnStartup = old;
            XdgAutostart.DirectoryOverride = null; WindowsStartupShortcut.DirectoryOverride = null;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    });
}
