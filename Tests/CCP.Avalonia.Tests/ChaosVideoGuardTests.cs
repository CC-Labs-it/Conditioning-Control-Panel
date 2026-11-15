using System;
using System.Threading;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Xunit;
using App = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public class ChaosVideoGuardTests
{
    private sealed class Host : IMandatoryVideoHost
    {
        public int Shows, Closes;
        public void Show(string path, bool strict) => Shows++;
        public double CloseAll() { Closes++; return 0; }
        public void ShowMessage(AttentionVerdict verdict, int ms, Action then) { }
    }
    private sealed class Clock : TimeProvider
    {
        private Timer? _timer;
        public void Fire() => _timer?.Fire();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            => _timer = new Timer(() => callback(state));
        private sealed class Timer(Action callback) : ITimer
        {
            private bool _disposed;
            public void Fire() { if (!_disposed) callback(); }
            public bool Change(TimeSpan dueTime, TimeSpan period) => !_disposed;
            public void Dispose() => _disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return default; }
        }
    }

    [Fact]
    public void CapCancelsPendingWithoutCrediting() => AvaloniaTestDispatcher.Run(() =>
    {
        var clock = new Clock(); var host = new Host(); int credit = 0;
        var now = DateTime.UnixEpoch;
        var video = new MandatoryVideoScheduler(host, clock, () => new[] { "/chaos.mp4" });
        var guard = new ChaosVideoGuard(video, () => now, () => credit++);
        try
        {
            Assert.True(video.Trigger(userEarned: true, chaosToken: guard.Arm()));
            now = now.AddSeconds(15); Assert.False(guard.Tick(false)); clock.Fire();
            Assert.False(video.IsPlaying); Assert.Equal(0, host.Shows); Assert.Equal(0, credit);
        }
        finally { guard.Stop(); video.ForceCleanup(); }
    });

    [Theory]
    [InlineData("cap", 1)]
    [InlineData("closing", 0)]
    [InlineData("natural", 1)]
    [InlineData("dismiss", 0)]
    public void GuardCutsAndCreditsOnlyEnduredTapes(string ending, int expectedCredit) => AvaloniaTestDispatcher.Run(() =>
    {
        var oldPost = CoreDispatch.PostProvider;
        var oldFreeze = CoreSubliminal.BambiFreezeProvider;
        var oldAttention = CoreSettings.Current.AttentionChecksEnabled;
        var clock = new Clock(); var host = new Host();
        var video = new MandatoryVideoScheduler(host, clock, () => new[] { "/chaos.mp4" });
        var now = DateTime.UnixEpoch; int credit = 0;
        var guard = new ChaosVideoGuard(video, () => now, () => credit++);
        video.VideoStarted += guard.Started; video.VideoEnded += guard.Ended;
        try
        {
            CoreDispatch.PostProvider = a => a(); CoreSubliminal.BambiFreezeProvider = null;
            CoreSettings.Current.AttentionChecksEnabled = false;
            int token = guard.Arm();
            video.ArmRandomSegment(token, 15);
            Assert.True(video.Trigger(userEarned: true, chaosToken: token));
            clock.Fire();
            Assert.Equal(1, host.Shows);
            if (ending == "natural") video.Ended();
            else if (ending == "dismiss") video.End();
            else
            {
                now = now.AddSeconds(ending == "cap" ? 15 : 3);
                Assert.True(guard.Tick(ending == "closing"));
            }
            Assert.Equal(expectedCredit, credit);
            Assert.False(video.IsPlaying);
            Assert.Equal(1, host.Closes);
            guard.Stop();
            Assert.Equal(expectedCredit, credit);
        }
        finally
        {
            video.ForceCleanup(); CoreDispatch.PostProvider = oldPost;
            CoreSubliminal.BambiFreezeProvider = oldFreeze;
            CoreSettings.Current.AttentionChecksEnabled = oldAttention;
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DetonationRunTickCutsAndCreditsTheLesson(bool closing) => AvaloniaTestDispatcher.Run(() =>
    {
        var oldVideo = CoreEngine.Video; var oldFirer = EffectPayload.Firer;
        var oldPost = CoreDispatch.PostProvider; var oldFreeze = CoreSubliminal.BambiFreezeProvider;
        var oldClock = BubbleOverlay.Field.Clock;
        var heavy = typeof(ChaosRunHost).GetField("_heavyUntilUtc", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var oldHeavy = heavy.GetValue(null);
        var quarantine = typeof(ChaosRunHost).GetField("_videoQuarantineUntilUtc", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var oldQuarantine = quarantine.GetValue(null);
        var meta = ChaosMeta.State;
        bool hadProgress = meta.LessonProgress.TryGetValue("porn_dvd", out var progress);
        bool completed = meta.LessonsComplete.Remove("porn_dvd");
        var clock = new Clock(); var host = new Host(); var now = DateTime.UtcNow.AddDays(1);
        var video = new MandatoryVideoScheduler(host, clock, () => new[] { "/chaos.mp4" });
        try
        {
            meta.LessonProgress["porn_dvd"] = 0;
            CoreDispatch.PostProvider = a => a(); CoreSubliminal.BambiFreezeProvider = null;
            CoreEngine.Video = video; EffectPayload.Firer = App.FireChaosPayload;
            BubbleOverlay.Field.Clock = () => now;
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = false });
            ChaosRunHost.Overlay!.FinishCountdown(); ChaosRunHost.StopTimerForTests();
            ChaosRunHost.FirePayloadForDetonation(new EffectBubbleSpec { Payload = new VideoPayload() });
            clock.Fire();
            Assert.Equal(1, host.Shows);
            if (closing) ChaosRunHost.State!.ElapsedSec = ChaosRunHost.State.RunDurationSec - 3.25;
            now = now.AddSeconds(closing ? 1 : 15);
            ChaosRunHost.Tick();
            Assert.False(video.IsPlaying);
            Assert.Equal(1, host.Closes); Assert.Contains(ConditioningControlPanel.Localization.Loc.Get("chaos_tape_snaps"), ChaosRunHost.State!.RecentEvents);
            Assert.Equal(closing ? 0 : 1, ChaosLessons.Progress("porn_dvd"));
            ChaosRunHost.ForceShutdown();
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = false });
            ChaosRunHost.Overlay!.FinishCountdown(); ChaosRunHost.StopTimerForTests();
            now = now.AddSeconds(1);
            ChaosRunHost.FirePayloadForDetonation(new EffectBubbleSpec { Payload = new VideoPayload() });
            Assert.False(video.IsPlaying); // teardown quarantine survives a run boundary
            now = now.AddSeconds(3);
            ChaosRunHost.FirePayloadForDetonation(new EffectBubbleSpec { Payload = new VideoPayload() });
            Assert.True(video.IsPlaying);
        }
        finally
        {
            ChaosRunHost.ForceShutdown(); video.ForceCleanup(); CoreEngine.Video = oldVideo;
            EffectPayload.Firer = oldFirer; CoreDispatch.PostProvider = oldPost;
            CoreSubliminal.BambiFreezeProvider = oldFreeze; BubbleOverlay.Field.Clock = oldClock;
            heavy.SetValue(null, oldHeavy); quarantine.SetValue(null, oldQuarantine);
            if (hadProgress) meta.LessonProgress["porn_dvd"] = progress; else meta.LessonProgress.Remove("porn_dvd");
            if (completed) meta.LessonsComplete.Add("porn_dvd");
        }
    });

    [Fact]
    public void VideoSelectionFailureDoesNotEscapeThePayload() => AvaloniaTestDispatcher.Run(() =>
    {
        var old = CoreEngine.Video;
        var video = new MandatoryVideoScheduler(new Host(), library: () => throw new InvalidOperationException("library"));
        try
        {
            CoreEngine.Video = video;
            App.FireChaosPayload(new VideoPayload());
            Assert.False(video.IsPlaying);
        }
        finally { video.ForceCleanup(); CoreEngine.Video = old; }
    });

    [Fact]
    public void AServiceAppearingMidRunDoesNotStartAnUnownedChaosTape() => AvaloniaTestDispatcher.Run(() =>
    {
        var oldVideo = CoreEngine.Video; var oldFirer = EffectPayload.Firer;
        var oldClock = BubbleOverlay.Field.Clock;
        var heavy = typeof(ChaosRunHost).GetField("_heavyUntilUtc", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var oldHeavy = heavy.GetValue(null);
        var clock = new Clock(); var host = new Host();
        var video = new MandatoryVideoScheduler(host, clock, () => new[] { "/chaos.mp4" });
        try
        {
            CoreEngine.Video = null; EffectPayload.Firer = App.FireChaosPayload;
            BubbleOverlay.Field.Clock = () => DateTime.UtcNow.AddDays(2);
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = false });
            ChaosRunHost.Overlay!.FinishCountdown(); ChaosRunHost.StopTimerForTests();
            CoreEngine.Video = video;
            ChaosRunHost.FirePayloadForDetonation(new EffectBubbleSpec { Payload = new VideoPayload() });
            clock.Fire();
            Assert.False(video.IsPlaying);
            Assert.Equal(0, host.Shows);
        }
        finally
        {
            ChaosRunHost.ForceShutdown(); video.ForceCleanup(); CoreEngine.Video = oldVideo;
            EffectPayload.Firer = oldFirer; BubbleOverlay.Field.Clock = oldClock; heavy.SetValue(null, oldHeavy);
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RunExitCancelsDeferredRequestAndKeepsUserVideo(bool panic) => AvaloniaTestDispatcher.Run(() =>
    {
        var oldVideo = CoreEngine.Video; var oldFirer = EffectPayload.Firer;
        var oldPost = CoreDispatch.PostProvider; var oldFreeze = CoreSubliminal.BambiFreezeProvider;
        var oldClock = BubbleOverlay.Field.Clock;
        var heavy = typeof(ChaosRunHost).GetField("_heavyUntilUtc", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var oldHeavy = heavy.GetValue(null);
        var quarantine = typeof(ChaosRunHost).GetField("_videoQuarantineUntilUtc", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var oldQuarantine = quarantine.GetValue(null);
        var clock = new Clock(); var host = new Host(); Action? held = null;
        var video = new MandatoryVideoScheduler(host, clock, () => new[] { "/chaos.mp4" });
        try
        {
            CoreDispatch.PostProvider = a => a(); CoreSubliminal.BambiFreezeProvider = null;
            CoreEngine.Video = video; EffectPayload.Firer = App.FireChaosPayload;
            BubbleOverlay.Field.Clock = () => DateTime.UtcNow.AddDays(1);
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = false });
            ChaosRunHost.Overlay!.FinishCountdown(); ChaosRunHost.StopTimerForTests();
            video.DeferTrigger = action => { held = action; return true; };
            ChaosRunHost.FirePayloadForDetonation(new EffectBubbleSpec { Payload = new VideoPayload() });
            Assert.NotNull(held);
            video.DeferTrigger = null;
            Assert.True(video.Trigger(path: "/user.mp4")); clock.Fire();
            Assert.Equal(1, host.Shows);
            if (panic) ChaosRunHost.ForceShutdown(); else ChaosRunHost.RequestStop();
            Assert.True(video.IsPlaying);
            Assert.Equal(1, host.Shows);
            Assert.Equal(0, host.Closes);
            video.End();
            held!(); clock.Fire();
            Assert.False(video.IsPlaying);
            Assert.Equal(1, host.Shows);
        }
        finally
        {
            ChaosRunHost.ForceShutdown(); video.ForceCleanup(); CoreEngine.Video = oldVideo;
            EffectPayload.Firer = oldFirer; CoreDispatch.PostProvider = oldPost;
            CoreSubliminal.BambiFreezeProvider = oldFreeze; BubbleOverlay.Field.Clock = oldClock;
            heavy.SetValue(null, oldHeavy); quarantine.SetValue(null, oldQuarantine);
        }
    });
}
