using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ProfileLoadedRepaintTests
{
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, true)]
    public Task ProfileLoadedRepaintsAndResumesOnlyWhenWanted(bool optIn, bool alreadyRunning, bool expected) =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            var settings = new SettingsService();
            CoreSettings.ServiceProvider = () => settings;
            var s = CoreSettings.Current;
            s.MotionLevel = MotionLevel.Off;
            s.AutonomyResumeOnStartup = false;
            s.AutonomyModeEnabled = false;
            s.AutonomyConsentGiven = true;
            s.PlayerLevel = 2;
            s.PlayerXP = 1;
            CoreEntitlement.HasPremiumProvider = () => true;
            CoreEntitlement.IsFreeTodayProvider = _ => false;
            var shell = new MainShellWindow();
            try
            {
                shell.Show(); Dispatcher.UIThread.RunJobs();
                s.AutonomyResumeOnStartup = optIn;
                s.AutonomyModeEnabled = true;
                if (alreadyRunning) Assert.True(shell.Autonomy.Start(withTimer: false));
                var changes = 0;
                shell.Autonomy.EnabledChanged += (_, _) => changes++;
                s.PlayerLevel = 17;
                s.PlayerXP = 23;
                var field = typeof(AccountSeed).GetField("ProfileLoaded", BindingFlags.Static | BindingFlags.NonPublic)!;
                ((Action?)field.GetValue(null))?.Invoke();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(expected, shell.Autonomy.IsEnabled);
                Assert.Equal(optIn && !alreadyRunning ? 1 : 0, changes);
                Assert.Equal("LVL 17", shell.FindControl<TextBlock>("TxtLevelLabel")!.Text);
                Assert.StartsWith("23 / ", shell.FindControl<TextBlock>("TxtXP")!.Text);
            }
            finally { shell.Close(); Dispatcher.UIThread.RunJobs(); }
            return Task.CompletedTask;
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PendingProfileLoadCannotRestartAfterPanicOrClose(bool close) =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            var settings = new SettingsService();
            CoreSettings.ServiceProvider = () => settings;
            var s = CoreSettings.Current;
            s.AutonomyResumeOnStartup = false;
            s.AutonomyModeEnabled = false;
            s.AutonomyConsentGiven = true;
            CoreEntitlement.HasPremiumProvider = () => true;
            CoreEntitlement.IsFreeTodayProvider = _ => false;
            var shell = new MainShellWindow();
            try
            {
                shell.Show(); Dispatcher.UIThread.RunJobs();
                s.AutonomyResumeOnStartup = true;
                s.AutonomyModeEnabled = true;
                s.PlayerLevel = 17;
                var starts = 0;
                shell.Autonomy.EnabledChanged += (_, on) => { if (on) starts++; };
                var field = typeof(AccountSeed).GetField("ProfileLoaded", BindingFlags.Static | BindingFlags.NonPublic)!;
                ((Action?)field.GetValue(null))?.Invoke();
                if (close) shell.Close();
                else shell.StopAutonomyForPanic();
                Assert.True(s.AutonomyModeEnabled);
                Dispatcher.UIThread.RunJobs();
                Assert.False(shell.Autonomy.IsEnabled);
                Assert.Equal(0, starts);
                if (!close)
                {
                    Assert.Equal("LVL 17", shell.FindControl<TextBlock>("TxtLevelLabel")!.Text);
                    // A later profile load remains eligible; panic invalidates only the queued work.
                    ((Action?)field.GetValue(null))?.Invoke();
                    Dispatcher.UIThread.RunJobs();
                    Assert.True(shell.Autonomy.IsEnabled);
                    Assert.Equal(1, starts);
                }
            }
            finally { shell.Close(); Dispatcher.UIThread.RunJobs(); }
            return Task.CompletedTask;
        });

}
