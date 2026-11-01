using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Haptics;
using ConditioningControlPanel.Services.Haptics.Core;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class MandatoryVideoRemoteTests
{
    [Fact]
    public void Playback_rechecks_the_cache_and_never_streams_an_evicted_scrolller_clip()
    {
        Assert.Null(MandatoryVideoOverlay.PlaybackPath("https://v.scrolller.com/never-cached-test.mp4"));
        Assert.Equal("/cache.mp4", MandatoryVideoOverlay.PlaybackPath("https://v.scrolller.com/cached.mp4", _ => "/cache.mp4"));
        Assert.Equal("/local.mp4", MandatoryVideoOverlay.PlaybackPath("/local.mp4"));
        Assert.Equal("https://example.com/clip.mp4", MandatoryVideoOverlay.PlaybackPath("https://example.com/clip.mp4"));
        Assert.True(MandatoryVideoOverlay.Instance.Scheduler.RemoteMedia);
    }
    [Fact]
    public void Scrolller_clip_has_a_credit_mark_but_local_video_does_not() => AvaloniaTestDispatcher.Run(() =>
    {
        Assert.True(MandatoryVideoOverlay.CreditFor("https://v.scrolller.com/clip.mp4").Shown);
        const string cdn = "https://example.com/credited-test.mp4";
        Assert.True(MandatoryVideoOverlay.CreditFor(cdn, _ => new ScrolllerCacheMeta("clip", "test")).Shown);
        Assert.False(MandatoryVideoOverlay.CreditFor("/local.mp4").Shown);
    });
    private sealed class Host : IMandatoryVideoHost
    {
        public int Closed;
        public void Show(string path, bool strict) { }
        public double CloseAll() { Closed++; return 0; }
        public void ShowMessage(AttentionVerdict verdict, int ms, Action then) { }
    }

    private sealed class StillTime : TimeProvider
    {
        private Never? _timer;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan due, TimeSpan period) => _timer = new Never(callback, state);
        public void Fire() => _timer!.Fire();
        private sealed class Never(TimerCallback callback, object? state) : ITimer
        {
            public void Fire() => callback(state);
            public bool Change(TimeSpan due, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => default;
        }
    }

    [Fact]
    public void EvictedClipNeverAnnouncesOrStartsHapticsAfterPreRoll() => AvaloniaTestDispatcher.Run(() =>
    {
        using var h = new HapticService(new HapticSettings { VideoEnabled = true, VideoIntensity = 0.8, FunScriptEnabled = true });
        var oldHaptics = CoreHaptics.Service; var oldPost = CoreDispatch.PostProvider;
        CoreHaptics.Service = h; CoreDispatch.PostProvider = action => action();
        var o = (MandatoryVideoOverlay)Activator.CreateInstance(typeof(MandatoryVideoOverlay), nonPublic: true)!;
        var clock = new StillTime(); var v = new MandatoryVideoScheduler(o, clock, Array.Empty<string>); o.Scheduler = v;
        var started = 0; v.VideoStarted += () => started++;
        try
        {
            h.StartVideoBackgroundVibeAsync().GetAwaiter().GetResult();
            Assert.True(h.GetLayer(HapticLayer.Video) > 0); // positive control
            h.StopVideoBackgroundVibeAsync().GetAwaiter().GetResult();
            var seq = typeof(FunScriptService).GetField("_loadSeq", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var before = (long)seq.GetValue(h.FunScript)!;
            Assert.True(v.Trigger(path: $"https://v.scrolller.com/evicted-{Guid.NewGuid():N}.mp4"));
            clock.Fire(); Dispatcher.UIThread.RunJobs();
            Assert.False(v.IsPlaying); Assert.Equal(0, h.GetLayer(HapticLayer.Video));
            Assert.Null(v.LastVideoPath); Assert.Equal(0, started); Assert.False(h.FunScript.IsActive);
            Assert.Equal(before + 1, (long)seq.GetValue(h.FunScript)!); // stop only, no pending script load
        }
        finally { v.ForceCleanup(); CoreHaptics.Service = oldHaptics; CoreDispatch.PostProvider = oldPost; }
    });

    // No native player is needed to exercise GuardTick: the scheduler's frozen pre-roll never fires.
    // The source path is the URL selected by the scheduler, even when playback resolves to a cache file.
    private static void WithRemoteGuard(Action<MandatoryVideoOverlay, MandatoryVideoScheduler, Host> check)
    {
        var o = (MandatoryVideoOverlay)Activator.CreateInstance(typeof(MandatoryVideoOverlay), nonPublic: true)!;
        var fields = BindingFlags.Instance | BindingFlags.NonPublic;
        var path = typeof(MandatoryVideoOverlay).GetField("_path", fields)!;
        var surfaces = (List<MandatoryVideoOverlay.Surface>)o.Surfaces;
        Assert.Empty(surfaces);
        var host = new Host();
        var scheduler = new MandatoryVideoScheduler(host, new StillTime(), Array.Empty<string>);
        const string url = "https://v.scrolller.com/cached.mp4";
        Assert.Equal("/cache.mp4", MandatoryVideoOverlay.PlaybackPath(url, _ => "/cache.mp4"));
        o.Scheduler = scheduler;
        surfaces.Add(new(null!, null!, null!, null!, null!, null!, null!, null!));
        path.SetValue(o, url);
        try
        {
            Assert.True(scheduler.Trigger(path: url));
            scheduler.NoteSpawn();
            check(o, scheduler, host);
        }
        finally
        {
            scheduler.ForceCleanup();
            surfaces.Clear();
        }
    }

    [Fact]
    public void CachedRemoteUrlKeepsItsTwentySecondStartGrace() => AvaloniaTestDispatcher.Run(() =>
        WithRemoteGuard((o, scheduler, host) =>
        {
            var first = typeof(MandatoryVideoOverlay).GetProperty("FirstFrameMs", BindingFlags.Instance | BindingFlags.NonPublic)!;
            first.SetValue(o, -1L);
            // Ten seconds of window/player setup precede Play; the watchdog starts at Play.
            typeof(MandatoryVideoOverlay).GetField("_guardStartedAt", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(o, 10d);
            o.GuardTick(18); Assert.True(scheduler.IsPlaying);
            o.GuardTick(29.99); Assert.True(scheduler.IsPlaying);
            o.GuardTick(30); Assert.False(scheduler.IsPlaying);
            Assert.Equal(1, host.Closed);
            Assert.Equal(1, scheduler.AttentionSpawned); // ending does not enter the replay path
        }));

    [Fact]
    public void CachedRemoteUrlEndsWithoutReplayWhenFramesStop() => AvaloniaTestDispatcher.Run(() =>
        WithRemoteGuard((o, scheduler, host) =>
        {
            var first = typeof(MandatoryVideoOverlay).GetProperty("FirstFrameMs", BindingFlags.Instance | BindingFlags.NonPublic)!;
            first.SetValue(o, 0L);
            o.FrameTs = Stopwatch.GetTimestamp() - 6 * Stopwatch.Frequency;
            o.GuardTick(25);
            Assert.False(scheduler.IsPlaying);
            Assert.Equal(1, host.Closed);
            Assert.Equal(1, scheduler.AttentionSpawned); // a replay would reset the checks
        }));

}
