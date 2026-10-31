using System;
using System.Collections.Generic;
using System.IO;
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
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class EngineStartSchedulerTests
{
    private static void Setup()
    {
        if (Application.Current == null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public void StartButtonAndSchedulerRespectTheManualStopHold() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var previous = CoreSettings.ServiceProvider;
        var oldStop = CoreEngine.StoppedHook;
        var oldSink = OsNotifications.Sink;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = service.Current;
        var now = DateTime.Now;
        s.SchedulerEnabled = true;
        s.SchedulerStartTime = now.AddHours(-1).ToString("HH:mm");
        s.SchedulerEndTime = now.AddHours(1).ToString("HH:mm");
        s.SchedulerMonday = s.SchedulerTuesday = s.SchedulerWednesday = s.SchedulerThursday = true;
        s.SchedulerFriday = s.SchedulerSaturday = s.SchedulerSunday = true;
        s.FlashEnabled = s.SubliminalEnabled = s.BouncingTextEnabled = s.BubblesEnabled = s.LockCardEnabled = false;
        var notices = new List<string>();
        OsNotifications.Sink = (title, body) => notices.Add(title + "|" + body);
        var shell = new MainShellWindow { TrayHostPresent = () => false };
        try
        {
            shell.Show(); Dispatcher.UIThread.RunJobs();
            Assert.NotNull(typeof(MainShellWindow).GetField("_schedulerGrace", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(shell));
            CoreEngine.StoppedHook = shell.OnEngineStopped;
            shell.SchedulerStartupCheck(now);
            Assert.True(CoreEngine.IsRunning);
            Assert.Contains(Loc.Get("notif_scheduler_active") + "|" + Loc.Get("notif_scheduler_autostarted"), notices);
            var button = shell.Named<Button>("BtnStart")!;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(CoreEngine.IsRunning);
            Assert.True(shell.Scheduler.ManuallyStopped);
            shell.SchedulerTick(now);
            Assert.False(CoreEngine.IsRunning);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(CoreEngine.IsRunning);
            Assert.False(shell.Scheduler.ManuallyStopped);
            shell.SchedulerTick(now.AddHours(3));
            Assert.False(CoreEngine.IsRunning);
            Assert.Contains(Loc.Get("notif_scheduler") + "|" + Loc.Get("notif_scheduler_ended"), notices);
        }
        finally
        {
            CoreEngine.Stop(); CoreEngine.StoppedHook = oldStop; OsNotifications.Sink = oldSink;
            shell.Close(); CoreSettings.ServiceProvider = previous;
        }
    });

    [Fact]
    public void AudioOnlyStartAndStopRespectStandaloneBedAndReleaseDucking() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var oldSettings = CoreSettings.ServiceProvider;
        var oldAudio = LayeredAudio.Instance;
        var oldMixer = LibVlcAudio.Instance;
        var oldStart = CoreEngine.StartedHook;
        var oldStop = CoreEngine.StoppedHook;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var file = Path.GetTempFileName();
        var player = new Player();
        using var audio = new LayeredAudio(_ => player = new Player());
        var mixer = new LibVlcAudio(_ => "[]");
        LayeredAudio.Instance = audio; LibVlcAudio.Instance = mixer;
        var s = service.Current;
        s.AudioOnlySession = true; s.AudioLayersEnabled = false;
        s.AudioLayers.Add(new AudioLayerTrack { Path = file, Enabled = true, Volume = 50 });
        var shell = new MainShellWindow();
        try
        {
            CoreEngine.StartedHook = shell.OnEngineStarted; CoreEngine.StoppedHook = shell.OnEngineStopped;
            CoreEngine.Start();
            Assert.True(audio.IsPlaying);
            mixer.Duck(80);
            Assert.True(mixer.IsDucked);
            CoreEngine.Stop();
            Assert.False(mixer.IsDucked);
            Assert.False(audio.IsPlaying);
            Assert.True(player.Disposed);
            s.AudioLayersEnabled = true;
            CoreEngine.Start(); CoreEngine.Stop();
            Assert.True(audio.IsPlaying);
            Assert.False(player.Disposed);
            audio.Stop(); s.AudioOnlySession = false;
            CoreEngine.Start();
            Assert.False(audio.IsPlaying);
        }
        finally
        {
            CoreEngine.Stop(); CoreEngine.StartedHook = oldStart; CoreEngine.StoppedHook = oldStop;
            shell.Close(); LayeredAudio.Instance = oldAudio; LibVlcAudio.Instance = oldMixer;
            CoreSettings.ServiceProvider = oldSettings; File.Delete(file);
        }
    });

    [Fact]
    public void TakeoverStartsOnlyWithConsentAndOutlivesStopOnlyWhileArmed() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var oldSettings = CoreSettings.ServiceProvider;
        var oldPremium = CoreEntitlement.HasPremiumProvider;
        var oldStart = CoreEngine.StartedHook;
        var oldStop = CoreEngine.StoppedHook;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreEntitlement.HasPremiumProvider = () => true;
        var shell = new MainShellWindow();
        try
        {
            CoreEngine.StartedHook = shell.OnEngineStarted; CoreEngine.StoppedHook = shell.OnEngineStopped;
            service.Current.AudioOnlySession = true;
            service.Current.AutonomyModeEnabled = true;
            service.Current.AutonomyConsentGiven = false;
            CoreEngine.Start();
            Assert.False(shell.Autonomy.IsEnabled);
            CoreEngine.Stop();
            service.Current.AutonomyConsentGiven = true;
            CoreEngine.Start();
            Assert.True(shell.Autonomy.IsEnabled);
            CoreEngine.Stop();
            Assert.True(shell.Autonomy.IsEnabled);
            service.Current.AutonomyModeEnabled = false;
            CoreEngine.Stop();
            Assert.False(shell.Autonomy.IsEnabled);
        }
        finally
        {
            CoreEngine.Stop(); shell.Close(); CoreSettings.ServiceProvider = oldSettings;
            CoreEntitlement.HasPremiumProvider = oldPremium;
            CoreEngine.StartedHook = oldStart; CoreEngine.StoppedHook = oldStop;
        }
    });

    [Fact]
    public void ExpiredScheduleCancelsAStartWaitingForThePortal() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var oldSettings = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var shell = new MainShellWindow();
        var generation = typeof(MainShellWindow).GetField("_engineGen", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        try
        {
            service.Current.SchedulerEnabled = true; service.Current.SchedulerThursday = true;
            service.Current.SchedulerStartTime = "16:00"; service.Current.SchedulerEndTime = "22:00";
            shell.Scheduler.AutoStarted = true;
            var before = (int)generation.GetValue(null)!;
            shell.SchedulerTick(new DateTime(2026, 10, 22, 12, 0, 0)); // default 16:00-22:00
            Assert.Equal(before + 1, (int)generation.GetValue(null)!);
        }
        finally { shell.Close(); CoreSettings.ServiceProvider = oldSettings; }
    });

    [Fact]
    public void AudioOnlyRunSuppressesPersistentVisualOverlays() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var oldSettings = CoreSettings.ServiceProvider;
        var oldRunning = CoreSession.IsEngineRunningProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreSession.IsEngineRunningProvider = () => CoreEngine.IsRunning;
        var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        var pink = typeof(ConditioningControlPanel.Avalonia.Views.Overlays.PinkFilterOverlay).GetMethod("ShouldShow", flags)!;
        var spiral = typeof(ConditioningControlPanel.Avalonia.Views.Overlays.SpiralOverlay).GetMethod("ShouldShow", flags)!;
        try
        {
            service.Current.AudioOnlySession = true;
            CoreEngine.Start();
            Assert.False((bool)pink.Invoke(null, null)!);
            Assert.False((bool)spiral.Invoke(null, null)!);
            service.Current.AudioOnlySession = false; // the start-time choice holds for this run
            Assert.False((bool)pink.Invoke(null, null)!);
            CoreEngine.Stop(); CoreEngine.Start();
            Assert.True((bool)pink.Invoke(null, null)!);
            Assert.True((bool)spiral.Invoke(null, null)!);
        }
        finally { CoreEngine.Stop(); CoreSession.IsEngineRunningProvider = oldRunning; CoreSettings.ServiceProvider = oldSettings; }
    });

    private sealed class Player : LayeredAudio.ILayerPlayer
    {
        public int Volume { private get; set; }
        internal bool Disposed;
        public void Dispose() => Disposed = true;
    }
}
