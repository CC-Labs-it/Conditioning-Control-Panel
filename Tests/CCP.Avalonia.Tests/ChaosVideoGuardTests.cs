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
        public int Shows, Closes; public Action? Then; public bool BubbleCountActive { get; set; }
        public void Show(string path, bool strict) => Shows++;
        public double CloseAll() { Closes++; return 0; }
        public void ShowMessage(AttentionVerdict verdict, int ms, Action then) => Then = then;
    }
    private sealed class Clock : TimeProvider
    {
        private Timer? _timer; public int Created;
        public void Fire() => _timer?.Fire();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) { Created++; return _timer = new Timer(() => callback(state)); }
        private sealed class Timer(Action callback) : ITimer
        {
            private bool _disposed;
            public void Fire() { if (!_disposed) callback(); }
            public bool Change(TimeSpan dueTime, TimeSpan period) => !_disposed;
            public void Dispose() => _disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return default; }
        }
    }
    [Theory] [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)]
    public void CapCreditsUnstartedRequestAndCancelsPending(bool empty, bool closing) => AvaloniaTestDispatcher.Run(() =>
    {
        var clock = new Clock(); var host = new Host(); int credit = 0; var now = DateTime.UnixEpoch;
        var video = new MandatoryVideoScheduler(host, clock, () => empty ? Array.Empty<string>() : new[] { "/chaos.mp4" });
        var guard = new ChaosVideoGuard(video, () => now, () => credit++);
        try {
            Assert.Equal(!empty, video.Trigger(userEarned: true, silentIfEmpty: true, chaosToken: guard.Arm()));
            now = now.AddSeconds(closing ? 3 : 14); Assert.False(guard.Tick(closing)); Assert.Equal(0, credit);
            if (!closing) { now = now.AddSeconds(1); Assert.False(guard.Tick(false)); } clock.Fire();
            Assert.False(video.IsPlaying); Assert.Equal(0, host.Shows); Assert.Equal(closing ? 0 : 1, credit);
            Assert.False(guard.Armed); Assert.False(guard.Tick(false)); guard.Stop(); Assert.Equal(closing ? 0 : 1, credit);
        }
        finally { guard.Stop(); video.ForceCleanup(); }
    });
    [Theory]
    [InlineData("cap", 1)] [InlineData("closing", 0)] [InlineData("natural", 0)] [InlineData("natural-at-cap", 0)] [InlineData("natural-after-cap", 0)] [InlineData("dismiss", 0)]
    public void GuardCutsAndCreditsOnlyEnduredTapes(string ending, int expectedCredit) => AvaloniaTestDispatcher.Run(() =>
    {
        var oldPost = CoreDispatch.PostProvider; var oldFreeze = CoreSubliminal.BambiFreezeProvider;
        var oldAttention = CoreSettings.Current.AttentionChecksEnabled; var clock = new Clock(); var host = new Host();
        var video = new MandatoryVideoScheduler(host, clock, () => new[] { "/chaos.mp4" }); var now = DateTime.UnixEpoch; int credit = 0;
        var guard = new ChaosVideoGuard(video, () => now, () => credit++); video.VideoStarted += guard.Started; video.VideoEnded += guard.Ended;
        try {
            CoreDispatch.PostProvider = a => a(); CoreSubliminal.BambiFreezeProvider = null;
            CoreSettings.Current.AttentionChecksEnabled = false;
            int token = guard.Arm(); video.ArmRandomSegment(token, 15);
            Assert.True(video.Trigger(userEarned: true, chaosToken: token)); clock.Fire();
            Assert.Equal(1, host.Shows);
            if (ending.StartsWith("natural")) { now = now.AddSeconds(ending == "natural" ? 3 : ending == "natural-at-cap" ? 15 : 16); video.Ended(); Assert.False(guard.Armed); now = now.AddSeconds(20); Assert.False(guard.Tick(false)); }
            else if (ending == "dismiss") video.End();
            else
            {
                now = now.AddSeconds(ending == "cap" ? 15 : 3); Assert.True(guard.Tick(ending == "closing"));
            }
            Assert.Equal(expectedCredit, credit); Assert.False(video.IsPlaying);
            Assert.Equal(1, host.Closes); guard.Stop();
            Assert.Equal(expectedCredit, credit);
        }
        finally {
            video.ForceCleanup(); CoreDispatch.PostProvider = oldPost; CoreSubliminal.BambiFreezeProvider = oldFreeze;
            CoreSettings.Current.AttentionChecksEnabled = oldAttention;
        }
    });
    [Theory]
    [InlineData("natural")] [InlineData("playing")] [InlineData("preroll")] [InlineData("cap")] [InlineData("closing")] [InlineData("exit")] [InlineData("cascade")] [InlineData("bubble")] [InlineData("user")] [InlineData("mercy")] [InlineData("running")] [InlineData("twice")] [InlineData("empty")] [InlineData("quiet")] [InlineData("quiet-schedule")]
    public void AttentionRetryRemainsOwnedUntilCompletionOrCancellation(string mode) => AvaloniaTestDispatcher.Run(() =>
    {
        var s = CoreSettings.Current; var oldSettings = (s.AttentionChecksEnabled, s.MercySystemEnabled, s.MercyAfterFails, s.FlashEnabled, s.MandatoryVideosEnabled);
        var oldPost = CoreDispatch.PostProvider; var oldFreeze = CoreSubliminal.BambiFreezeProvider;
        var oldPause = CoreBubbles.PauseAction; var oldResume = CoreBubbles.ResumeAction; var oldFlash = (CoreFlash.StartProvider, CoreFlash.StopProvider, CoreFlash.IsRunning);
        var clock = new Clock(); var host = new Host(); var now = DateTime.UnixEpoch; int credit = 0, resumes = 0, flashes = 0, emptyPicks = 0; bool rain = false, empty = false;
        var video = new MandatoryVideoScheduler(host, clock, () => { if (empty) { emptyPicks++; return Array.Empty<string>(); } return new[] { "/chaos.mp4" }; }) { CascadeIsRaining = () => rain };
        var guard = new ChaosVideoGuard(video, () => now, () => credit++); video.VideoStarted += guard.Started; video.VideoEnded += guard.Ended;
        try {
            (s.AttentionChecksEnabled, s.MercySystemEnabled, s.MercyAfterFails) = (true, mode == "mercy", 2); CoreDispatch.PostProvider = a => a(); CoreSubliminal.BambiFreezeProvider = null;
            CoreBubbles.PauseAction = () => { }; CoreBubbles.ResumeAction = () => resumes++; if (mode is "running" or "quiet" or "quiet-schedule") { ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet(); s.FlashEnabled = true; s.MandatoryVideosEnabled = true; CoreFlash.StartProvider = () => flashes++; CoreFlash.StopProvider = () => { }; video.Start(); }
            int token = guard.Arm(); video.ArmRandomSegment(token, 15); Assert.True(video.Trigger(true, userEarned: true, chaosToken: token)); clock.Fire();
            video.NoteSpawn(); video.Ended(); Assert.True(guard.Armed); Assert.Equal(0, credit); Assert.NotNull(host.Then);
            if (mode is "natural" or "playing" or "preroll" or "mercy" or "twice")
            {
                host.Then!(); Assert.Equal(token, video.PlayingChaosToken); Assert.Equal(15, video.RandomSegmentSeconds);
                if (mode == "preroll") { guard.Stop(); clock.Fire(); Assert.Equal(1, host.Shows); }
                else { clock.Fire(); Assert.Equal(2, host.Shows);
                    if (mode == "natural") video.Ended();
                    else if (mode == "mercy") { video.NoteSpawn(); video.Ended(); host.Then!(); }
                    else if (mode == "twice") { video.NoteSpawn(); video.Ended(); Assert.True(guard.Armed); Assert.Equal(0, credit); host.Then!(); clock.Fire(); Assert.Equal(3, host.Shows); Assert.Equal(token, video.PlayingChaosToken); video.Ended(); }
                    else { now = now.AddSeconds(15); guard.Tick(false); } Assert.Equal(mode == "playing" ? 1 : 0, credit); }
            }
            else if (mode == "empty") { empty = true; typeof(MandatoryVideoScheduler).GetProperty(nameof(MandatoryVideoScheduler.LastVideoPath))!.SetValue(video, null); host.Then!(); Assert.Equal(1, resumes); guard.Stop(); Assert.Equal(1, resumes); Assert.Equal(0, credit); }
            else if (mode == "user")
            {
                Assert.True(video.Trigger(path: "/user.mp4")); clock.Fire(); guard.Stop(); host.Then!();
                Assert.True(video.IsPlaying); Assert.Equal(1, host.Closes); Assert.Equal(0, resumes); video.End();
            }
            else
            {
                if (mode is "cascade" or "bubble") { rain = mode == "cascade"; host.BubbleCountActive = mode == "bubble"; host.Then!(); }
                now = now.AddSeconds(mode == "cap" ? 15 : 1); if (mode.StartsWith("quiet")) ConditioningControlPanel.Services.Safety.PanicAftermath.ArmQuiet(); if (mode is "exit" or "running" or "quiet" or "quiet-schedule" or "cascade" or "bubble") guard.Stop(); else guard.Tick(mode == "closing");
                rain = false; host.BubbleCountActive = false; if (mode is not ("cascade" or "bubble")) host.Then!(); if (mode is not ("running" or "quiet" or "quiet-schedule")) clock.Fire();
                Assert.Equal(mode == "cap" ? 1 : 0, credit); if (mode == "exit") Assert.Equal(0, video.Penalties); if (mode == "quiet-schedule") Assert.Equal(2, clock.Created); Assert.Equal(mode.StartsWith("quiet") ? 0 : 1, resumes); Assert.Equal(1, host.Shows); if (mode == "bubble") { empty = true; host.BubbleCountActive = true; Assert.True(video.Trigger(silentIfEmpty: true)); host.BubbleCountActive = false; clock.Fire(); Assert.Equal(1, emptyPicks); Assert.Equal(1, resumes); }
            }
            Assert.False(guard.Armed); Assert.False(video.IsPlaying); if (mode is "running" or "quiet" or "quiet-schedule") { Assert.Equal(0, flashes); Assert.Equal(mode.StartsWith("quiet") ? 2 : 3, clock.Created); }
        }
        finally {
            guard.Stop(); video.Stop(); if (mode is "running" or "quiet" or "quiet-schedule") { if (mode.StartsWith("quiet")) ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet(); CoreFlash.Stop(); if (oldFlash.IsRunning) CoreFlash.Start(); (CoreFlash.StartProvider, CoreFlash.StopProvider) = (oldFlash.StartProvider, oldFlash.StopProvider); } (s.AttentionChecksEnabled, s.MercySystemEnabled, s.MercyAfterFails, s.FlashEnabled, s.MandatoryVideosEnabled) = oldSettings;
            CoreDispatch.PostProvider = oldPost; CoreSubliminal.BambiFreezeProvider = oldFreeze; CoreBubbles.PauseAction = oldPause; CoreBubbles.ResumeAction = oldResume;
        }
    });
    [Theory] [InlineData(false, false, false)] [InlineData(true, false, false)] [InlineData(false, true, false)] [InlineData(false, false, true)]
    public void DetonationRunTickCutsAndCreditsTheLesson(bool closing, bool natural, bool empty) => AvaloniaTestDispatcher.Run(() =>
    {
        var oldVideo = CoreEngine.Video; var oldFirer = EffectPayload.Firer; var oldAttention = CoreSettings.Current.AttentionChecksEnabled;
        var oldPost = CoreDispatch.PostProvider; var oldFreeze = CoreSubliminal.BambiFreezeProvider; var oldClock = BubbleOverlay.Field.Clock;
        var heavy = typeof(ChaosRunHost).GetField("_heavyUntilUtc", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var oldHeavy = heavy.GetValue(null);
        var quarantine = typeof(ChaosRunHost).GetField("_videoQuarantineUntilUtc", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var oldQuarantine = quarantine.GetValue(null); var meta = ChaosMeta.State;
        bool hadProgress = meta.LessonProgress.TryGetValue("porn_dvd", out var progress);
        bool completed = meta.LessonsComplete.Remove("porn_dvd");
        var clock = new Clock(); var host = new Host(); var now = DateTime.UtcNow.AddDays(1);
        var video = new MandatoryVideoScheduler(host, clock, () => empty ? Array.Empty<string>() : new[] { "/chaos.mp4" });
        try {
            meta.LessonProgress["porn_dvd"] = 0; CoreSettings.Current.AttentionChecksEnabled = false;
            CoreDispatch.PostProvider = a => a(); CoreSubliminal.BambiFreezeProvider = null;
            CoreEngine.Video = video; EffectPayload.Firer = App.FireChaosPayload; BubbleOverlay.Field.Clock = () => now;
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = false });
            ChaosRunHost.Overlay!.FinishCountdown(); ChaosRunHost.StopTimerForTests();
            ChaosRunHost.FirePayloadForDetonation(new EffectBubbleSpec { Payload = new VideoPayload() }); clock.Fire();
            Assert.Equal(empty ? 0 : 1, host.Shows);
            if (natural) { now = now.AddSeconds(3); video.Ended(); } else if (closing) ChaosRunHost.State!.ElapsedSec = ChaosRunHost.State.RunDurationSec - 3.25;
            if (empty) { now = now.AddSeconds(14); ChaosRunHost.Tick(); Assert.Equal(0, ChaosLessons.Progress("porn_dvd")); now = now.AddSeconds(1); } else now = now.AddSeconds(closing ? 1 : 15); ChaosRunHost.Tick();
            Assert.False(video.IsPlaying);
            Assert.Equal(empty ? 0 : 1, host.Closes); if (!natural && !empty) Assert.Contains(ConditioningControlPanel.Localization.Loc.Get("chaos_tape_snaps"), ChaosRunHost.State!.RecentEvents);
            Assert.Equal(closing || natural ? 0 : 1, ChaosLessons.Progress("porn_dvd")); if (natural || empty) return; ChaosRunHost.ForceShutdown();
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = false });
            ChaosRunHost.Overlay!.FinishCountdown(); ChaosRunHost.StopTimerForTests(); now = now.AddSeconds(1);
            ChaosRunHost.FirePayloadForDetonation(new EffectBubbleSpec { Payload = new VideoPayload() });
            Assert.False(video.IsPlaying); // teardown quarantine survives a run boundary
            now = now.AddSeconds(3); ChaosRunHost.FirePayloadForDetonation(new EffectBubbleSpec { Payload = new VideoPayload() });
            Assert.True(video.IsPlaying);
        }
        finally {
            ChaosRunHost.ForceShutdown(); video.ForceCleanup(); CoreEngine.Video = oldVideo; CoreSettings.Current.AttentionChecksEnabled = oldAttention;
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
        try {
            CoreEngine.Video = video;
            App.FireChaosPayload(new VideoPayload());
            Assert.False(video.IsPlaying);
        }
        finally { video.ForceCleanup(); CoreEngine.Video = old; }
    });
    [Fact]
    public void AServiceAppearingMidRunDoesNotStartAnUnownedChaosTape() => AvaloniaTestDispatcher.Run(() =>
    {
        var oldVideo = CoreEngine.Video; var oldFirer = EffectPayload.Firer; var oldClock = BubbleOverlay.Field.Clock;
        var heavy = typeof(ChaosRunHost).GetField("_heavyUntilUtc", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var oldHeavy = heavy.GetValue(null); var clock = new Clock(); var host = new Host();
        var video = new MandatoryVideoScheduler(host, clock, () => new[] { "/chaos.mp4" });
        try {
            CoreEngine.Video = null; EffectPayload.Firer = App.FireChaosPayload; BubbleOverlay.Field.Clock = () => DateTime.UtcNow.AddDays(2);
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = false });
            ChaosRunHost.Overlay!.FinishCountdown(); ChaosRunHost.StopTimerForTests(); CoreEngine.Video = video;
            ChaosRunHost.FirePayloadForDetonation(new EffectBubbleSpec { Payload = new VideoPayload() }); clock.Fire();
            Assert.False(video.IsPlaying); Assert.Equal(0, host.Shows);
        }
        finally {
            ChaosRunHost.ForceShutdown(); video.ForceCleanup(); CoreEngine.Video = oldVideo;
            EffectPayload.Firer = oldFirer; BubbleOverlay.Field.Clock = oldClock; heavy.SetValue(null, oldHeavy);
        }
    });
    [Theory] [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public void RunExitCancelsDeferredRequestAndKeepsUserVideo(bool panic, bool retry) => AvaloniaTestDispatcher.Run(() =>
    {
        var oldVideo = CoreEngine.Video; var oldFirer = EffectPayload.Firer;
        var oldPost = CoreDispatch.PostProvider; var oldFreeze = CoreSubliminal.BambiFreezeProvider; var oldClock = BubbleOverlay.Field.Clock;
        var settings = CoreSettings.Current; var oldSettings = (settings.AttentionChecksEnabled, settings.MandatoryVideoReplayAfterFailedCheck, settings.MercySystemEnabled);
        var heavy = typeof(ChaosRunHost).GetField("_heavyUntilUtc", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var oldHeavy = heavy.GetValue(null);
        var quarantine = typeof(ChaosRunHost).GetField("_videoQuarantineUntilUtc", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var oldQuarantine = quarantine.GetValue(null); var clock = new Clock(); var host = new Host(); Action? held = null;
        var video = new MandatoryVideoScheduler(host, clock, () => new[] { "/chaos.mp4" });
        try {
            CoreDispatch.PostProvider = a => a(); CoreSubliminal.BambiFreezeProvider = null; (settings.AttentionChecksEnabled, settings.MandatoryVideoReplayAfterFailedCheck, settings.MercySystemEnabled) = (true, true, false);
            CoreEngine.Video = video; EffectPayload.Firer = App.FireChaosPayload; BubbleOverlay.Field.Clock = () => DateTime.UtcNow.AddDays(1);
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = false });
            ChaosRunHost.Overlay!.FinishCountdown(); ChaosRunHost.StopTimerForTests();
            if (retry) { long lesson = ChaosLessons.Progress("porn_dvd"); ChaosRunHost.FirePayloadForDetonation(new EffectBubbleSpec { Payload = new VideoPayload() }); clock.Fire(); video.NoteSpawn(); video.Ended(); held = host.Then; Assert.NotNull(held); Assert.Equal(lesson, ChaosLessons.Progress("porn_dvd"));
                if (panic) ChaosRunHost.ForceShutdown(); else ChaosRunHost.RequestStop(); held!(); clock.Fire(); Assert.False(video.IsPlaying); Assert.Equal(1, host.Shows); Assert.Equal(lesson, ChaosLessons.Progress("porn_dvd")); return; }
            video.DeferTrigger = action => { held = action; return true; };
            ChaosRunHost.FirePayloadForDetonation(new EffectBubbleSpec { Payload = new VideoPayload() }); Assert.NotNull(held);
            video.DeferTrigger = null; Assert.True(video.Trigger(path: "/user.mp4")); clock.Fire();
            Assert.Equal(1, host.Shows);
            if (panic) ChaosRunHost.ForceShutdown(); else ChaosRunHost.RequestStop();
            Assert.True(video.IsPlaying); Assert.Equal(1, host.Shows);
            Assert.Equal(0, host.Closes); video.End();
            held!(); clock.Fire();
            Assert.False(video.IsPlaying); Assert.Equal(1, host.Shows);
        }
        finally {
            ChaosRunHost.ForceShutdown(); video.ForceCleanup(); CoreEngine.Video = oldVideo;
            EffectPayload.Firer = oldFirer; CoreDispatch.PostProvider = oldPost;
            CoreSubliminal.BambiFreezeProvider = oldFreeze; BubbleOverlay.Field.Clock = oldClock;
            heavy.SetValue(null, oldHeavy); quarantine.SetValue(null, oldQuarantine); (settings.AttentionChecksEnabled, settings.MandatoryVideoReplayAfterFailedCheck, settings.MercySystemEnabled) = oldSettings;
        }
    });
}
