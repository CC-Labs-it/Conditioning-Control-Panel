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
}
