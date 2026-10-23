using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
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

    [Fact]
    public void MuteCutsTheLiveClipButKeepsItsCaptionAndUnmuteDoesNotReplay() => Run(shell =>
    {
        var s = CoreSettings.Current;
        var saved = (s.MasterVolume, s.AvatarMuted, s.CompanionVoiceLinesMuted, CoreAudio.PlayStoppableProvider, s.AvatarEnabled);
        var clip = Path.GetTempFileName();
        var stopped = 0;
        shell.SetAvatarEnabled(true);
        var tube = shell.Tube!;
        try
        {
            (s.MasterVolume, s.AvatarMuted, s.CompanionVoiceLinesMuted) = (70, false, false);
            CoreAudio.PlayStoppableProvider = (_, _, _, _, finished) => () => { stopped++; finished?.Invoke(); };
            tube.GigglePriority("caption stays", phraseAudioPath: clip, barkVoice: true);
            Dispatcher.UIThread.RunJobs();
            shell.VoiceAction("mute")!();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, stopped);
            Assert.Equal("caption stays", tube.FindControl<TextBlock>("TxtSpeech")!.Text);
            shell.VoiceAction("unmute")!();
            Assert.Equal(1, stopped);
        }
        finally
        {
            (s.MasterVolume, s.AvatarMuted, s.CompanionVoiceLinesMuted, CoreAudio.PlayStoppableProvider, s.AvatarEnabled) = saved;
            File.Delete(clip);
        }
    });

    [Fact]
    public Task ConfirmationWaitsForTheClipAndTheEchoTail() => RunAsync(async shell =>
    {
        var clock = new SteppedClock();
        shell.VoiceTime = clock;
        var s = CoreSettings.Current;
        var saved = (s.MasterVolume, s.AvatarMuted, s.CompanionVoiceLinesMuted, CoreAudio.PlayStoppableProvider, s.AvatarEnabled);
        var clip = Path.GetTempFileName();
        Action? finished = null;
        shell.SetAvatarEnabled(true);
        try
        {
            (s.MasterVolume, s.AvatarMuted, s.CompanionVoiceLinesMuted) = (70, false, false);
            CoreAudio.PlayStoppableProvider = (_, _, _, _, done) => { finished = done; return () => done?.Invoke(); };
            shell.VoiceSay("confirmation", clip);
            Dispatcher.UIThread.RunJobs();
            var quiet = shell.VoiceWaitQuiet(true);
            Assert.False(quiet.IsCompleted);
            Assert.NotNull(finished);
            clock.Advance(TimeSpan.FromMilliseconds(400)); // a fixed 300 ms wait would already have completed
            Assert.False(quiet.IsCompleted);
            finished();
            Dispatcher.UIThread.RunJobs();
            Assert.False(quiet.IsCompleted); // still owes the 300 ms echo tail
            clock.Advance(TimeSpan.FromMilliseconds(299));
            Assert.False(quiet.IsCompleted);
            clock.Advance(TimeSpan.FromMilliseconds(1));
            await quiet.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            (s.MasterVolume, s.AvatarMuted, s.CompanionVoiceLinesMuted, CoreAudio.PlayStoppableProvider, s.AvatarEnabled) = saved;
            File.Delete(clip);
        }
    });

    private sealed class SteppedClock : TimeProvider
    {
        private long _now;
        private readonly List<Timer> _timers = new();
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => _now;
        public override ITimer CreateTimer(TimerCallback cb, object? state, TimeSpan due, TimeSpan period)
        {
            var t = new Timer(this, cb, state, _now + (long)due.TotalMilliseconds);
            lock (_timers) _timers.Add(t);
            return t;
        }
        public void Advance(TimeSpan by)
        {
            _now += (long)by.TotalMilliseconds;
            Timer[] due;
            lock (_timers) { due = _timers.Where(t => t.Due <= _now).ToArray(); _timers.RemoveAll(due.Contains); }
            foreach (var t in due) t.Cb(t.State);
        }
        private sealed class Timer(SteppedClock c, TimerCallback cb, object? state, long due) : ITimer
        {
            public readonly TimerCallback Cb = cb; public readonly object? State = state; public readonly long Due = due;
            public bool Change(TimeSpan d, TimeSpan p) => false;
            public void Dispose() { lock (c._timers) c._timers.Remove(this); }
            public ValueTask DisposeAsync() { Dispose(); return default; }
        }
    }

    private static void Run(Action<MainShellWindow> body) =>
        RunAsync(shell => { body(shell); return Task.CompletedTask; }).GetAwaiter().GetResult();

    private static Task RunAsync(Func<MainShellWindow, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var shell = new MainShellWindow();
        shell.Show();
        Dispatcher.UIThread.RunJobs();
        try { await body(shell); }
        finally
        {
            shell.Close();
            Dispatcher.UIThread.RunJobs();
        }
    });
}
