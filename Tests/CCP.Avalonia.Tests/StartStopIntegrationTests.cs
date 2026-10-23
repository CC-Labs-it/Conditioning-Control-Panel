using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;
using OsNotify = ConditioningControlPanel.Avalonia.Platform.OsNotifications;

namespace CCP.Avalonia.Tests;

// WPF MainWindow.StartStop.cs BtnStart_Click / StartEngine / scheduler, driven through the shell's
// own Start button: Relapse (a Start within 10 s of the user's Stop), the scheduler's manual-stop
// hold, the schedule's auto start + stop, and the ramp-finished stop.
public sealed class StartStopIntegrationTests
{
    private static readonly System.Reflection.PropertyInfo AchievementsProp = typeof(AvApp).GetProperty("Achievements",
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!;

    [Fact]
    public Task StartButtonDrivesRelapseSchedulerHoldAndAutoStartStop() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var dir = Directory.CreateTempSubdirectory("ccp-startstop-").FullName;
        var oldSettings = CoreSettings.ServiceProvider;
        var oldAch = AchievementsProp.GetValue(null);
        var (oldStop, oldStart, oldSink) = (CoreEngine.StoppedHook, CoreEngine.StartedHook, OsNotify.Sink);
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.FlashEnabled = s.SubliminalEnabled = s.BouncingTextEnabled = s.BubblesEnabled = s.LockCardEnabled = false;
        s.MandatoryVideosEnabled = s.BubbleCountEnabled = s.PopQuizEnabled = s.MindWipeEnabled = false;
        var now = DateTime.Now;
        s.SchedulerEnabled = true;
        s.SchedulerStartTime = now.AddHours(-1).ToString("HH:mm");
        s.SchedulerEndTime = now.AddHours(1).ToString("HH:mm");
        s.SchedulerMonday = s.SchedulerTuesday = s.SchedulerWednesday = s.SchedulerThursday = true;
        s.SchedulerFriday = s.SchedulerSaturday = s.SchedulerSunday = true;
        var notified = new List<string>();
        OsNotify.Sink = (t, b) => notified.Add(t + "|" + b);
        var ach = new AchievementEngine(new AchievementStore(Path.Combine(dir, "achievements.json")));
        AchievementsProp.SetValue(null, ach);
        var shell = new MainShellWindow { TrayHostPresent = () => false };
        try
        {
            shell.Show(); Dispatcher.UIThread.RunJobs();
            CoreEngine.StoppedHook = shell.OnEngineStopped;
            CoreEngine.StartedHook = shell.OnEngineStarted;
            var start = shell.Named<Button>("BtnStart")!;

            start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(CoreEngine.IsRunning);
            Assert.False(ach.Progress.IsUnlocked("relapse"));   // no stop before it

            start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));   // the user's own Stop, inside the window
            Assert.False(CoreEngine.IsRunning);
            Assert.NotNull(ach.Progress.LastPanicPressTime);
            Assert.True(shell.Scheduler.ManuallyStopped);
            shell.SchedulerTick(now);
            Assert.False(CoreEngine.IsRunning);   // the schedule does not restart what the user stopped

            start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));   // back within 10 s
            Assert.True(ach.Progress.IsUnlocked("relapse"));
            Assert.False(shell.Scheduler.ManuallyStopped);
            start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            // A fresh window: the schedule starts the engine and stops only what it started.
            shell.Scheduler.ManuallyStopped = false;
            shell.SchedulerStartupCheck(now);
            Assert.True(CoreEngine.IsRunning);
            Assert.Contains(Loc.Get("notif_scheduler_active") + "|" + Loc.Get("notif_scheduler_autostarted"), notified);
            shell.SchedulerTick(now.AddHours(3));
            Assert.False(CoreEngine.IsRunning);
            Assert.Contains(Loc.Get("notif_scheduler") + "|" + Loc.Get("notif_scheduler_ended"), notified);

            // The ramp's end of run is the shell's stop, with WPF's tray notice.
            start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            shell.OnRampComplete();
            Assert.False(CoreEngine.IsRunning);
            Assert.Contains(Loc.Get("notif_ramp_complete") + "|" + Loc.Get("notif_ramp_complete_body"), notified);
        }
        finally
        {
            CoreEngine.Stop();
            (CoreEngine.StoppedHook, CoreEngine.StartedHook, OsNotify.Sink) = (oldStop, oldStart, oldSink);
            AchievementsProp.SetValue(null, oldAch);
            shell.Close(); CoreSettings.ServiceProvider = oldSettings;
            Directory.Delete(dir, recursive: true);
        }
        return Task.CompletedTask;
    });
}
