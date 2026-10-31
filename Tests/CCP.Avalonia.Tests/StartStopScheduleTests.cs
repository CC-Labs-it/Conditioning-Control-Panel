using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;
using OsNotify = ConditioningControlPanel.Avalonia.Platform.OsNotifications;
namespace CCP.Avalonia.Tests;
public sealed class StartStopScheduleTests
{
    [Fact]
    public void ScheduleStartsStopsAndHonoursTheUsersStop() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = service.Current;
        var (oldNow, oldInterval) = (CoreIntensityRamp.Now, CoreIntensityRamp.Interval);
        s.FlashEnabled = s.SubliminalEnabled = s.BouncingTextEnabled = s.BubblesEnabled = s.LockCardEnabled = false;
        s.MandatoryVideosEnabled = s.BubbleCountEnabled = s.PopQuizEnabled = s.MindWipeEnabled = false;
        s.IntensityRampEnabled = true;
        s.SchedulerEnabled = true;
        var now = DateTime.Now;
        s.SchedulerStartTime = now.AddHours(-1).ToString("HH:mm");
        s.SchedulerEndTime = now.AddHours(1).ToString("HH:mm");
        s.SchedulerMonday = s.SchedulerTuesday = s.SchedulerWednesday = s.SchedulerThursday = true;
        s.SchedulerFriday = s.SchedulerSaturday = s.SchedulerSunday = true;
        var notices = new List<string>();
        OsNotify.Sink = (title, body) => notices.Add(title);
        var shell = new MainShellWindow { TrayHostPresent = () => false };
        Window? audioWindow = null;
        try
        {
            shell.Show();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var graceField = typeof(MainShellWindow).GetField("_schedulerGrace", flags)!;
            var grace = graceField.GetValue(shell);
            Assert.NotNull(grace);
            Assert.Null(typeof(MainShellWindow).GetField("_schedulerTimer", flags)!.GetValue(shell));
            shell.Hide(); shell.Show();
            Assert.Same(grace, graceField.GetValue(shell));
            CoreEngine.StoppedHook = shell.OnEngineStopped;
            shell.SchedulerStartupCheck(now);
            Assert.True(CoreEngine.IsRunning);
            Assert.True(CoreIntensityRamp.IsRunning);
            Assert.Contains(Loc.Get("notif_scheduler_active"), notices);
            shell.Named<Button>("BtnStart")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(CoreEngine.IsRunning);
            Assert.False(CoreIntensityRamp.IsRunning);
            Assert.True(shell.Scheduler.ManuallyStopped);
            shell.SchedulerTick(now);
            Assert.False(CoreEngine.IsRunning);
            shell.StartEngineFromUser();
            Assert.False(shell.Scheduler.ManuallyStopped);
            shell.SchedulerTick(now.AddHours(3));
            Assert.False(CoreEngine.IsRunning);
            Assert.Contains(Loc.Get("notif_scheduler"), notices);
            shell.StartEngine();
            shell.OnRampComplete();
            Assert.False(CoreEngine.IsRunning);
            Assert.Contains(Loc.Get("notif_ramp_complete"), notices);
            var audio = new AudioSettingsSection();
            audioWindow = new Window { Content = audio };
            audioWindow.Show();
            var master = audio.FindControl<Slider>("SliderMaster")!;
            var label = audio.FindControl<TextBlock>("TxtMaster")!;
            var clock = now;
            CoreIntensityRamp.Now = () => clock; CoreIntensityRamp.Interval = System.Threading.Timeout.InfiniteTimeSpan;
            s.RampMode = RampMode.Multiplier; s.RampCurve = RampCurve.Linear;
            s.SchedulerMultiplier = 2; s.RampDurationMinutes = 10;
            s.RampLinkMasterAudio = s.RampLinkPinkFilterOpacity = true;
            s.EndSessionOnRampComplete = false;
            foreach (var panic in new[] { false, true })
            {
                s.MasterVolume = 40; s.PinkFilterOpacity = 20; s.PinkFilterEnabled = false;
                CoreEngine.Start();
                clock = clock.AddMinutes(5); CoreIntensityRamp.Tick();
                Assert.Equal((60d, "60%", 30), (master.Value, label.Text, s.PinkFilterOpacity));
                typeof(MainShellWindow).GetMethod("PulsePinkFilter", flags)!.Invoke(shell, null);
                Assert.Equal(45, s.PinkFilterOpacity);
                if (panic) PanicSurfaces.StopAll("ramp regression", shell); else CoreEngine.Stop();
                Assert.Equal((40d, "40%", 20, false), (master.Value, label.Text, s.PinkFilterOpacity, s.PinkFilterEnabled));
            }
            audioWindow.Content = null; s.MasterVolume = 35;
            Assert.Equal(40d, master.Value); // detached binder no longer observes settings
            audioWindow.Content = audio; Assert.Equal(35d, master.Value);
            service.RestoreFrom(new AppSettings { MasterVolume = 25 });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal((25d, "25%"), (master.Value, label.Text));
            s.MasterVolume = 10; // replacement unhooks the old settings object
            Assert.Equal(25d, master.Value);
        }
        finally
        {
            CoreEngine.Stop(); audioWindow?.Close(); shell.Close();
            CoreIntensityRamp.Now = oldNow; CoreIntensityRamp.Interval = oldInterval;
        }
    });
}
