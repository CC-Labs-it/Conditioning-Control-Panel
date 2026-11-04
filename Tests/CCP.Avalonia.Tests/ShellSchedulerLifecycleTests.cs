using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ShellSchedulerLifecycleTests
{
    private static void Run(Action<MainShellWindow, SettingsService> test) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var previous = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = service.Current;
        s.Welcomed = s.HasAcceptedAgeVerification = true;
        s.SchedulerEnabled = true;
        s.SchedulerStartTime = "10:00";
        s.SchedulerEndTime = "11:00";
        var shell = new MainShellWindow();
        try { test(shell, service); }
        finally
        {
            shell.MarkExitRequestedForTest();
            shell.Close();
            shell.Tray?.Dispose();
            CoreEngine.Stop();
            CoreSettings.ServiceProvider = previous;
        }
    });

    [Fact]
    public void StartupAndBoundariesHideAndAnnounceOnce() => Run((shell, service) =>
    {
        var now = new DateTime(2026, 11, 2, 10, 0, 0);
        bool running = false;
        int starts = 0, stops = 0;
        var notices = new List<(string, string)>();
        var oldSink = OsNotifications.Sink;
        try
        {
            OsNotifications.Sink = (title, body) => notices.Add((title, body));
            shell.SchedulerNow = () => now;
            shell.SchedulerRunning = () => running;
            shell.SchedulerStartOverride = done => { starts++; running = true; done(); };
            shell.SchedulerStopOverride = () => { stops++; running = false; };
            shell.Show();
            shell.CreateTray();
            shell.TrayHostPresent = () => true;
            shell.SchedulerStartup();
            Assert.False(shell.IsVisible);
            Assert.Equal(1, starts);
            Assert.Equal((Loc.Get("scheduler_notice_active"), Loc.Get("scheduler_notice_started")), Assert.Single(notices));
            shell.SchedulerTick();
            Assert.Equal(1, starts);
            now = now.AddHours(1);
            shell.SchedulerTick();
            Assert.Equal(1, stops);
            Assert.Equal((Loc.Get("scheduler_notice_title"), Loc.Get("scheduler_notice_ended")), notices[1]);
            shell.SchedulerTick();
            Assert.Equal(2, notices.Count);
            shell.ShowFromTray();
            shell.TrayHostPresent = () => false;
            now = now.AddDays(1).AddHours(-1);
            shell.SchedulerTick();
            Assert.Equal(2, starts);
            Assert.True(shell.IsVisible);
        }
        finally { OsNotifications.Sink = oldSink; }
    });

    [Fact]
    public void SettingsChangesStartWithoutHidingAndCloseUnhooks() => Run((shell, service) =>
    {
        var s = service.Current;
        var now = new DateTime(2026, 11, 2, 10, 30, 0);
        int starts = 0, hidden = 0, stops = 0;
        bool running = false;
        shell.SchedulerNow = () => now;
        shell.SchedulerRunning = () => running;
        shell.SchedulerStartOverride = done => { starts++; running = true; done(); };
        shell.SchedulerStopOverride = () => { stops++; running = false; };
        shell.SchedulerMinimizeOverride = () => hidden++;
        shell.SchedulerNoticeOverride = (_, _) => { };
        s.SchedulerEnabled = false;
        shell.Show();
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var grace = (DispatcherTimer)typeof(MainShellWindow).GetField("_schedulerGrace", flags)!.GetValue(shell)!;
        var timer = (DispatcherTimer)typeof(MainShellWindow).GetField("_schedulerTimer", flags)!.GetValue(shell)!;
        Assert.Equal(TimeSpan.FromSeconds(60), grace.Interval);
        Assert.True(grace.IsEnabled);
        Assert.Equal(TimeSpan.FromSeconds(30), timer.Interval);
        shell.SchedulerStartup();
        Assert.False(grace.IsEnabled);
        Assert.True(timer.IsEnabled);
        timer.Interval = TimeSpan.FromDays(1); // test drives ticks itself
        Assert.Equal(0, starts);
        s.SchedulerStartTime = "12:00";
        s.SchedulerEndTime = "22:00";
        s.SchedulerEnabled = true;
        // The panel writes both fields in one event. Its intermediate window includes now,
        // but the final window does not and must never start a session.
        s.SchedulerStartTime = "09:00";
        s.SchedulerEndTime = "10:00";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, starts);
        s.SchedulerEndTime = "11:00";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, starts);
        Assert.Equal(0, hidden);
        s.SchedulerEndTime = "10:15";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, stops);
        shell.SchedulerTick();
        Assert.Equal(1, stops);
        shell.Close();
        Assert.False(timer.IsEnabled);
        s.SchedulerEndTime = "11:00";
        Dispatcher.UIThread.RunJobs();
        shell.SchedulerTick();
        Assert.Equal(1, starts);
    });

    [Fact]
    public void ManualStopAndPanicSuppressRestartUntilNextWindow() => Run((shell, service) =>
    {
        var now = new DateTime(2026, 11, 2, 10, 0, 0);
        int starts = 0;
        shell.SchedulerNow = () => now;
        shell.SchedulerStartOverride = done => { starts++; CoreEngine.Start(); done(); };
        shell.SchedulerMinimizeOverride = () => { };
        shell.SchedulerNoticeOverride = (_, _) => { };
        service.Current.AudioOnlySession = true;
        shell.Show();
        CoreEngine.Start(); // a manual run is not scheduler-owned
        shell.SchedulerStartup();
        Assert.True(CoreEngine.IsRunning);
        shell.Named<Button>("BtnStart")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(CoreEngine.IsRunning);
        shell.SchedulerTick();
        Assert.Equal(0, starts);
        now = now.AddHours(1); shell.SchedulerTick();
        now = now.AddDays(1).AddHours(-1); shell.SchedulerTick();
        Assert.Equal(1, starts);
        var old = PanicSurfaces.All;
        try
        {
            PanicSurfaces.All = new[] { new PanicSurfaces.Surface("engine", _ => MainShellWindow.StopEngine()) };
            PanicSurfaces.StopAll("test", shell);
            Assert.False(CoreEngine.IsRunning);
            shell.SchedulerTick();
            Assert.Equal(1, starts);
        }
        finally { PanicSurfaces.All = old; }
    });

    [Fact]
    public void BoundaryAndPanicCancelStartsWaitingOnPortal() => Run((shell, service) =>
    {
        var now = new DateTime(2026, 11, 2, 10, 0, 0);
        Action? pending = null;
        shell.SchedulerNow = () => now;
        shell.EngineStartDispatchOverride = action => pending = action;
        shell.SchedulerMinimizeOverride = () => { };
        shell.SchedulerNoticeOverride = (_, _) => { };
        shell.Show();
        shell.SchedulerStartup();
        Assert.NotNull(pending);
        now = now.AddHours(1); shell.SchedulerTick();
        pending!();
        Assert.False(CoreEngine.IsRunning);
        now = now.AddDays(1).AddHours(-1); shell.SchedulerTick();
        Assert.NotNull(pending);
        var old = PanicSurfaces.All;
        try
        {
            PanicSurfaces.All = new[] { new PanicSurfaces.Surface("engine", _ => MainShellWindow.StopEngine()) };
            PanicSurfaces.StopAll("test", shell);
            pending!();
            Assert.False(CoreEngine.IsRunning);
            shell.SchedulerTick();
            Assert.False(CoreEngine.IsRunning);
        }
        finally { PanicSurfaces.All = old; }
    });
    [Fact]
    public void PanicBeforeGraceAndDisablingPendingStartAreSafe() => Run((shell, service) =>
    {
        var now = new DateTime(2026, 11, 2, 10, 0, 0);
        Action? pending = null;
        int queued = 0;
        shell.SchedulerNow = () => now;
        shell.EngineStartDispatchOverride = action => { queued++; pending = action; };
        shell.SchedulerMinimizeOverride = () => { };
        shell.SchedulerNoticeOverride = (_, _) => { };
        shell.Show();
        var old = PanicSurfaces.All;
        try
        {
            PanicSurfaces.All = new[] { new PanicSurfaces.Surface("engine", _ => MainShellWindow.StopEngine()) };
            PanicSurfaces.StopAll("test", shell);
            shell.SchedulerStartup();
            Assert.Equal(0, queued);
            now = now.AddHours(1); shell.SchedulerTick();
            now = now.AddDays(1).AddHours(-1); shell.SchedulerTick();
            Assert.Equal(1, queued);
            service.Current.AudioOnlySession = true;
            CoreEngine.Start(); // another path started a session while the portal was pending
            service.Current.SchedulerEnabled = false;
            Assert.True(CoreEngine.IsRunning); // disabling a schedule must not stop that session
            CoreEngine.Stop(); // do not invalidate the queued callback here
            pending!();
            Assert.False(CoreEngine.IsRunning);
        }
        finally { PanicSurfaces.All = old; }
    });

    [Fact]
    public void DisablingScheduleDoesNotCancelANewerManualStart() => Run((shell, service) =>
    {
        var now = new DateTime(2026, 11, 2, 10, 0, 0);
        var queued = new List<Action>();
        shell.SchedulerNow = () => now;
        shell.EngineStartDispatchOverride = action => queued.Add(action);
        shell.SchedulerMinimizeOverride = () => { };
        shell.SchedulerNoticeOverride = (_, _) => { };
        service.Current.AudioOnlySession = true;
        service.Current.PinkFilterEnabled = service.Current.SpiralEnabled = false;
        shell.Show();
        shell.SchedulerStartup();
        shell.StartEngineFromUser();
        Assert.Equal(2, queued.Count);
        service.Current.SchedulerEnabled = false;
        queued[1](); // the user's newer start still runs
        Assert.True(CoreEngine.IsRunning);
        service.Current.SchedulerEnabled = true;
        now = now.AddHours(1);
        shell.SchedulerTick();
        Assert.True(CoreEngine.IsRunning); // the scheduler no longer owns this run
    });

    [Fact]
    public void ACoreSessionStartingWhileThePortalWaitsKeepsItsEngine() => Run((shell, service) =>
    {
        var now = new DateTime(2026, 11, 2, 10, 0, 0);
        Action? pending = null;
        int requests = 0;
        shell.SchedulerNow = () => now;
        shell.EngineStartDispatchOverride = action => { pending = action; requests++; };
        shell.SchedulerMinimizeOverride = () => { };
        shell.SchedulerNoticeOverride = (_, _) => { };
        service.Current.AudioOnlySession = true;
        shell.Show();
        shell.SchedulerStartup();
        Assert.NotNull(pending);
        CoreEngine.Start(); // SessionRunner can start Core directly while the portal waits
        now = now.AddHours(1);
        shell.SchedulerTick();
        Assert.True(CoreEngine.IsRunning);
        CoreEngine.Stop(); // no shell generation change
        pending!();
        Assert.False(CoreEngine.IsRunning);
        now = now.AddDays(1).AddHours(-1);
        shell.SchedulerTick();
        Assert.Equal(2, requests);
        var hook = CoreEngine.StoppedHook;
        try
        {
            CoreEngine.StoppedHook = shell.OnEngineStopped;
            CoreEngine.Start();
            CoreEngine.Stop(); // the Core session ends before the scheduler's next tick
            pending!();
            Assert.False(CoreEngine.IsRunning);
            shell.SchedulerTick();
            Assert.Equal(2, requests);
            shell.StartEngineFromUser();
            pending!();
            Assert.True(CoreEngine.IsRunning);
            now = now.AddHours(1);
            shell.SchedulerTick();
            Assert.True(CoreEngine.IsRunning); // the later manual run is not scheduler-owned
        }
        finally { CoreEngine.StoppedHook = hook; }
    });

    [Fact]
    public void AutomaticBoundariesStartAndStopTheRealEngine() => Run((shell, service) =>
    {
        var now = new DateTime(2026, 11, 2, 10, 0, 0);
        shell.SchedulerNow = () => now;
        shell.EngineStartDispatchOverride = action => action();
        shell.SchedulerMinimizeOverride = () => { };
        shell.SchedulerNoticeOverride = (_, _) => { };
        service.Current.AudioOnlySession = true;
        service.Current.PinkFilterEnabled = service.Current.SpiralEnabled = false;
        shell.Show();
        shell.SchedulerStartup();
        Assert.True(CoreEngine.IsRunning);
        now = now.AddHours(1);
        shell.SchedulerTick();
        Assert.False(CoreEngine.IsRunning);
    });

}
