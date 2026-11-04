using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public class TakeoverEngineLifecycleTests
{
    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public Task EngineStartAndStopRespectEligibility(bool enabled, bool consent, bool entitled) =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var s = CoreSettings.Current;
            s.AutonomyResumeOnStartup = false;
            s.PanicKeyEnabled = false;
            var shell = new MainShellWindow();
            shell.Show();
            try
            {
                CoreEntitlement.HasPremiumProvider = () => entitled;
                CoreEntitlement.IsFreeTodayProvider = _ => false;
                (s.AutonomyModeEnabled, s.AutonomyConsentGiven) = (enabled, consent);
                CoreEngine.StoppedHook = shell.OnEngineStopped;
                shell.StartEngine();
                Assert.Equal(enabled && consent && entitled, shell.Autonomy.IsEnabled);
                MainShellWindow.StopEngine();
                Assert.Equal(enabled && consent && entitled, shell.Autonomy.IsEnabled);
                if (shell.Autonomy.IsEnabled)
                {
                    s.AutonomyConsentGiven = false;
                    shell.StartEngine();
                    MainShellWindow.StopEngine();
                    Assert.False(shell.Autonomy.IsEnabled);
                }
            }
            finally { shell.Autonomy.Stop(); CoreEngine.Stop(); shell.RequestExit(); }
            return Task.CompletedTask;
        });

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public Task StopDropsTakeoverWhenEligibilityIsLost(int lost) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        s.AutonomyResumeOnStartup = false; s.PanicKeyEnabled = false;
        var shell = new MainShellWindow(); shell.Show();
        try
        {
            bool entitled = true;
            CoreEntitlement.HasPremiumProvider = () => entitled;
            CoreEntitlement.IsFreeTodayProvider = _ => false;
            s.AutonomyModeEnabled = s.AutonomyConsentGiven = true;
            CoreEngine.StoppedHook = shell.OnEngineStopped;
            shell.StartEngine(); Assert.True(shell.Autonomy.IsEnabled);
            if (lost == 0) s.AutonomyModeEnabled = false;
            if (lost == 1) s.AutonomyConsentGiven = false;
            if (lost == 2) entitled = false;
            MainShellWindow.StopEngine(); Assert.False(shell.Autonomy.IsEnabled);
        }
        finally { shell.Autonomy.Stop(); CoreEngine.Stop(); shell.RequestExit(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task StopCancelsPulseEvenWhenTakeoverRemainsEnabled() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        s.AutonomyResumeOnStartup = false; s.PanicKeyEnabled = false;
        var shell = new MainShellWindow(); shell.Show();
        try
        {
            CoreEntitlement.HasPremiumProvider = () => true;
            s.AutonomyModeEnabled = s.AutonomyConsentGiven = true;
            CoreEngine.StoppedHook = shell.OnEngineStopped;
            var timers = new List<Action>(); shell.PulseTimer = (a, _) => timers.Add(a);
            s.BubblesEnabled = false;
            int starts = 0, stops = 0;
            CoreBubbles.StartAction = () => starts++; CoreBubbles.StopAction = () => stops++;
            shell.StartEngine();
            shell.PerformAutonomy(AutonomyActionType.StartBubbles);
            Assert.Equal(1, starts); Assert.Single(timers);
            MainShellWindow.StopEngine();
            Assert.True(shell.Autonomy.IsEnabled); Assert.Equal(2, stops);
            timers[0](); Assert.Equal(2, stops);
        }
        finally { shell.Autonomy.Stop(); CoreEngine.Stop(); shell.RequestExit(); }
        return Task.CompletedTask;
    });
}
