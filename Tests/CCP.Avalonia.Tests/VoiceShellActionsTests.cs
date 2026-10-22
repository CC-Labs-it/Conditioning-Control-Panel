using System;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>The voice intents wire-views-tab-she-listening-b gave this head, driven through the shell's
/// own intent table (WPF AutonomyService.VoiceCommandHost.VoiceAction).</summary>
public sealed class VoiceShellActionsTests
{
    /// <summary>WPF ApplyVoiceMute / AdjustMasterVolume (MainWindow.Patreon.cs:2136, :2189).</summary>
    [Fact]
    public void VolumeAndMuteFollowWpf() => Run(shell =>
    {
        var s = CoreSettings.Current;
        var saved = (s.MasterVolume, s.SubAudioEnabled, s.AvatarMuted);
        try
        {
            (s.MasterVolume, s.SubAudioEnabled, s.AvatarMuted) = (50, true, false);
            shell.VoiceAction("louder")!();
            Assert.Equal(65, s.MasterVolume);
            shell.VoiceAction("quieter")!();
            shell.VoiceAction("quieter")!();
            Assert.Equal(35, s.MasterVolume);
            s.MasterVolume = 95;
            shell.VoiceAction("louder")!();
            Assert.Equal(100, s.MasterVolume);   // clamped
            s.MasterVolume = 40;
            shell.VoiceAction("mute")!();
            Assert.Equal((0, false, true), (s.MasterVolume, s.SubAudioEnabled, s.AvatarMuted));
            shell.VoiceAction("unmute")!();
            Assert.Equal((40, true, false), (s.MasterVolume, s.SubAudioEnabled, s.AvatarMuted));   // remembered, not 70
        }
        finally { (s.MasterVolume, s.SubAudioEnabled, s.AvatarMuted) = saved; }
    });

    /// <summary>WPF PauseSessionFromRemote / ResumeSessionFromRemote (MainWindow.RemoteControl.cs:1340).</summary>
    [Fact]
    public void PauseAndResumeDriveTheRunningSession() => Run(shell =>
    {
        var runner = AvApp.Sessions = new SessionRunner(new SessionLogService());
        try
        {
            shell.StartSession(new Session { Id = "voice_pause_test", Name = "Voice Pause", DurationMinutes = 5 });
            Assert.True(runner.IsRunning && !runner.IsPaused);
            shell.VoiceAction("pause")!();
            Dispatcher.UIThread.RunJobs();
            Assert.True(runner.IsPaused);
            shell.VoiceAction("resume")!();
            Dispatcher.UIThread.RunJobs();
            Assert.False(runner.IsPaused);
        }
        finally
        {
            runner.Stop();
            AvApp.Sessions = null;
            CoreSession.IsSessionRunningProvider = null;
            CoreEngine.Stop();
        }
    });

    /// <summary>WPF StopPinkFilter/StopSpiral drop a sustained hold with the overlay: an engine stop
    /// (panic, tray Stop) must not leave a voice "go pink" / spiral pinned on screen.</summary>
    [Fact]
    public void EngineStopDropsTheVoiceOverlayHolds() => Run(shell =>
    {
        var prev = MainShellWindow.CanShowDesktopOverlay;
        MainShellWindow.CanShowDesktopOverlay = () => true;
        try
        {
            shell.VoiceAction("pink_on")!();
            shell.VoiceAction("spiral_on")!();
            Assert.Equal(Math.Max(0.4, CoreSettings.Current.PinkFilterOpacity / 100.0), PinkFilterOverlay.SustainedOpacity);
            Assert.True(SpiralOverlay.SustainedHold);
            shell.OnEngineStopped();
            Assert.Null(PinkFilterOverlay.SustainedOpacity);
            Assert.False(SpiralOverlay.SustainedHold);
        }
        finally
        {
            MainShellWindow.CanShowDesktopOverlay = prev;
            (PinkFilterOverlay.SustainedOpacity, SpiralOverlay.SustainedHold) = (null, false);
        }
    });

    private static void Run(Action<MainShellWindow> body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var shell = new MainShellWindow();
        shell.Show();
        Dispatcher.UIThread.RunJobs();
        try { body(shell); }
        finally
        {
            shell.Close();
            Dispatcher.UIThread.RunJobs();
        }
    });
}
