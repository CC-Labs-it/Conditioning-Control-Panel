using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public class TakeoverEngineLifecycleTests
{
    /// <summary>Runs <paramref name="body"/> on a fresh shell and puts back every static seam and
    /// setting the tests touch, so no later test inherits a fake provider or a hook to a closed shell.</summary>
    private static Task WithShell(Action<MainShellWindow, AppSettings> body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var (premium, freeToday, hook) = (CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider, CoreEngine.StoppedHook);
        var (bubStart, bubStop) = (CoreBubbles.StartAction, CoreBubbles.StopAction);
        var (resume, panic, enabled, consent, bubbles) = (s.AutonomyResumeOnStartup, s.PanicKeyEnabled,
            s.AutonomyModeEnabled, s.AutonomyConsentGiven, s.BubblesEnabled);
        s.AutonomyResumeOnStartup = false; s.PanicKeyEnabled = false;
        MainShellWindow? shell = null;
        try
        {
            shell = new MainShellWindow(); shell.Show();
            CoreEngine.StoppedHook = shell.OnEngineStopped;
            body(shell, s);
        }
        finally
        {
            if (shell != null) { shell.Autonomy.Stop(); CoreEngine.Stop(); shell.RequestExit(); }
            (CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider, CoreEngine.StoppedHook) = (premium, freeToday, hook);
            (CoreBubbles.StartAction, CoreBubbles.StopAction) = (bubStart, bubStop);
            (s.AutonomyResumeOnStartup, s.PanicKeyEnabled, s.AutonomyModeEnabled, s.AutonomyConsentGiven, s.BubblesEnabled)
                = (resume, panic, enabled, consent, bubbles);
        }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public Task EngineStartAndStopRespectEligibility(bool enabled, bool consent, bool entitled) => WithShell((shell, s) =>
    {
        CoreEntitlement.HasPremiumProvider = () => entitled;
        CoreEntitlement.IsFreeTodayProvider = _ => false;
        (s.AutonomyModeEnabled, s.AutonomyConsentGiven) = (enabled, consent);
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
    });

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public Task StopDropsTakeoverWhenEligibilityIsLost(int lost) => WithShell((shell, s) =>
    {
        bool entitled = true;
        CoreEntitlement.HasPremiumProvider = () => entitled;
        CoreEntitlement.IsFreeTodayProvider = _ => false;
        s.AutonomyModeEnabled = s.AutonomyConsentGiven = true;
        shell.StartEngine(); Assert.True(shell.Autonomy.IsEnabled);
        if (lost == 0) s.AutonomyModeEnabled = false;
        if (lost == 1) s.AutonomyConsentGiven = false;
        if (lost == 2) entitled = false;
        MainShellWindow.StopEngine(); Assert.False(shell.Autonomy.IsEnabled);
    });

    /// <summary>WPF Presets.cs:1618: a session from idle starts the engine through BtnStart_Click, so
    /// StartEngine (StartStop.cs:416-422) also starts Takeover (not over a panic hold, see below).</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task SessionFromIdleStartsTakeoverWhenEligible(bool entitled) => WithShell((shell, s) =>
    {
        CoreEntitlement.HasPremiumProvider = () => entitled;
        CoreEntitlement.IsFreeTodayProvider = _ => false;
        s.AutonomyModeEnabled = s.AutonomyConsentGiven = true;
        var (oldRunner, oldProvider) = (global::ConditioningControlPanel.Avalonia.App.Sessions, CoreSession.IsSessionRunningProvider);
        var runner = global::ConditioningControlPanel.Avalonia.App.Sessions = new SessionRunner(new SessionLogService());
        try
        {
            Assert.False(shell.Autonomy.IsEnabled);
            shell.StartSession(new Session { Id = "takeover_lifecycle", Name = "Takeover", DurationMinutes = 10 });
            Assert.True(runner.IsRunning);
            Assert.Equal(entitled, shell.Autonomy.IsEnabled);
        }
        finally
        {
            runner.Stop();
            global::ConditioningControlPanel.Avalonia.App.Sessions = oldRunner;
            CoreSession.IsSessionRunningProvider = oldProvider;
        }
    });

    /// <summary>Restack b206 review: the trunk holds Takeover off after a panic until the user restarts it
    /// (StopAutonomyForPanic); neither an engine start nor a session from idle lifts that hold.</summary>
    [Fact]
    public Task EngineStartKeepsThePanicHoldOnTakeover() => WithShell((shell, s) =>
    {
        CoreEntitlement.HasPremiumProvider = () => true;
        CoreEntitlement.IsFreeTodayProvider = _ => false;
        s.AutonomyModeEnabled = s.AutonomyConsentGiven = true;
        shell.StartEngine(); Assert.True(shell.Autonomy.IsEnabled);
        shell.StopAutonomyForPanic();
        MainShellWindow.StopEngine();
        Assert.False(shell.Autonomy.IsEnabled);
        shell.StartEngine();
        Assert.True(CoreEngine.IsRunning);
        Assert.False(shell.Autonomy.IsEnabled);
    });

    [Fact]
    public Task StopCancelsPulseEvenWhenTakeoverRemainsEnabled() => WithShell((shell, s) =>
    {
        CoreEntitlement.HasPremiumProvider = () => true;
        s.AutonomyModeEnabled = s.AutonomyConsentGiven = true;
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
    });
}
