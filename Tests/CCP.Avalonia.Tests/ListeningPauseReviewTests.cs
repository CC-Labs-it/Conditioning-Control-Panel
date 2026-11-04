using System;
using System.Reflection;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class ListeningPauseReviewTests
{
    [Fact]
    public void OrdinaryStopDoesNotAcquireALocalPanicHold() => AvaloniaTestDispatcher.Run(() =>
    {
        var runner = AvApp.Sessions = new SessionRunner(new SessionLogService());
        try
        {
            runner.Start(new Session { Id = "review_pause", Name = "Pause", DurationMinutes = 5 });
            MainShellWindow.StopEngine();
            Assert.True(runner.IsPaused);
            Assert.False(MainShellWindow.LocalPanicHoldsSession);
            MainShellWindow.NoteLocalPanicPause();
            Assert.True(MainShellWindow.LocalPanicHoldsSession);
            runner.Resume(); // a local resume retires that pause
            runner.Pause();
            Assert.False(MainShellWindow.LocalPanicHoldsSession);
        }
        finally { runner.Stop(); AvApp.Sessions = null; CoreEngine.Stop(); }
    });

    [Fact]
    public void VoicePausedVideoStillExpiresWithoutAPlayerOrFonts() => AvaloniaTestDispatcher.Run(() =>
    {
        var overlay = MandatoryVideoOverlay.Instance;
        var previous = overlay.Scheduler;
        var clock = new Clock();
        var host = new Host();
        var scheduler = new MandatoryVideoScheduler(host);
        var voice = typeof(MandatoryVideoOverlay).GetField("_voicePaused", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var time = overlay.Time;
        try
        {
            overlay.Scheduler = scheduler;
            overlay.Time = clock;
            Assert.True(scheduler.Trigger(path: "/review.mp4"));
            overlay.SafetyDeadline.Arm(clock.GetUtcNow().UtcDateTime, 10);
            voice.SetValue(overlay, true);
            clock.Seconds = 14;
            overlay.GuardTick();
            Assert.True(scheduler.IsPlaying);
            clock.Seconds = 15;
            overlay.GuardTick();
            Assert.False(scheduler.IsPlaying);
            Assert.Equal(1, host.Closes);
        }
        finally
        {
            scheduler.Stop();
            voice.SetValue(overlay, false);
            overlay.SafetyDeadline.Reset();
            overlay.Time = time;
            overlay.Scheduler = previous;
        }
    });

    private sealed class Clock : TimeProvider
    {
        internal double Seconds;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddSeconds(Seconds);
    }

    private sealed class Host : IMandatoryVideoHost
    {
        internal int Closes;
        public void Show(string path, bool strict) { }
        public double CloseAll() { Closes++; return 0; }
        public void ShowMessage(AttentionVerdict verdict, int ms, Action then) => then();
    }
}
