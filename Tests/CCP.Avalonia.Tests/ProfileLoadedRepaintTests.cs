using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
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
                    // A request completing after panic must also leave Takeover stopped.
                    ((Action?)field.GetValue(null))?.Invoke();
                    Dispatcher.UIThread.RunJobs();
                    Assert.False(shell.Autonomy.IsEnabled);
                    Assert.Equal(0, starts);
                }
            }
            finally { shell.Close(); Dispatcher.UIThread.RunJobs(); }
            return Task.CompletedTask;
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task LateProfileDoesNotRestartUntilExplicitStart(bool voice) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var s = CoreSettings.Current;
        s.AutonomyResumeOnStartup = false;
        s.AutonomyModeEnabled = false;
        s.AutonomyConsentGiven = true;
        s.PlayerLevel = 2;
        CoreEntitlement.HasPremiumProvider = () => true;
        CoreEntitlement.IsFreeTodayProvider = _ => false;
        var shell = new MainShellWindow();
        try
        {
            shell.Show(); Dispatcher.UIThread.RunJobs();
            s.AutonomyResumeOnStartup = true;
            s.AutonomyModeEnabled = true;
            Assert.True(shell.Autonomy.Start(withTimer: false));
            shell.StopAutonomyForPanic();
            s.PlayerLevel = 17;
            s.PlayerXP = 23;
            var field = typeof(AccountSeed).GetField("ProfileLoaded", BindingFlags.Static | BindingFlags.NonPublic)!;
            ((Action?)field.GetValue(null))?.Invoke();
            Dispatcher.UIThread.RunJobs();
            Assert.False(shell.Autonomy.IsEnabled);
            Assert.Equal("LVL 17", shell.FindControl<TextBlock>("TxtLevelLabel")!.Text);
            Assert.True(s.AutonomyModeEnabled);
            if (voice)
            {
                var action = typeof(MainShellWindow).GetMethod("VoiceAction", BindingFlags.Instance | BindingFlags.NonPublic)!;
                ((Action)action.Invoke(shell, new object[] { "takeover_on" })!)();
            }
            else
            {
                var tab = shell.FindControl<Control>("BambiTakeoverTab")!;
                tab.FindControl<Button>("BtnAutonomyStartStop")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            Dispatcher.UIThread.RunJobs();
            Assert.True(shell.Autonomy.IsEnabled);
            shell.Autonomy.Stop();
            ((Action?)field.GetValue(null))?.Invoke();
            Dispatcher.UIThread.RunJobs();
            Assert.True(shell.Autonomy.IsEnabled);
        }
        finally { shell.Close(); Dispatcher.UIThread.RunJobs(); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PanicBlocksOtherAutomaticStarts(bool restoreWindow) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var s = CoreSettings.Current;
        s.AutonomyResumeOnStartup = false;
        s.AutonomyModeEnabled = false;
        s.AutonomyConsentGiven = true;
        s.AvatarEnabled = false;
        CoreEntitlement.HasPremiumProvider = () => true;
        CoreEntitlement.IsFreeTodayProvider = _ => false;
        var shell = new MainShellWindow();
        try
        {
            shell.Show(); Dispatcher.UIThread.RunJobs();
            s.AutonomyResumeOnStartup = true;
            s.AutonomyModeEnabled = true;
            Assert.True(shell.Autonomy.Start(withTimer: false));
            if (restoreWindow)
            {
                var recovery = (WindowMinimizeRecovery)typeof(MainShellWindow)
                    .GetField("_windowMinimizeRecovery", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
                recovery.Minimized(true, true, false, false);
                typeof(MainShellWindow).GetMethod("InitializeAvatarTube", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shell, null);
            }
            shell.StopAutonomyForPanic();
            var method = restoreWindow ? "OnShellStateForTube" : "ResumeAutonomyOnStartup";
            typeof(MainShellWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shell, null);
            Assert.False(shell.Autonomy.IsEnabled);
        }
        finally { shell.Close(); Dispatcher.UIThread.RunJobs(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task FailedUserStartKeepsPanicHold() => AvaloniaTestDispatcher.RunAsync(() =>
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
            shell.StopAutonomyForPanic();
            CoreEntitlement.HasPremiumProvider = () => false;
            var action = typeof(MainShellWindow).GetMethod("VoiceAction", BindingFlags.Instance | BindingFlags.NonPublic)!;
            ((Action)action.Invoke(shell, new object[] { "takeover_on" })!)();
            Assert.False(shell.Autonomy.IsEnabled);
            CoreEntitlement.HasPremiumProvider = () => true;
            var field = typeof(AccountSeed).GetField("ProfileLoaded", BindingFlags.Static | BindingFlags.NonPublic)!;
            ((Action?)field.GetValue(null))?.Invoke();
            Dispatcher.UIThread.RunJobs();
            Assert.False(shell.Autonomy.IsEnabled);
        }
        finally { shell.Close(); Dispatcher.UIThread.RunJobs(); }
        return Task.CompletedTask;
    });

}
