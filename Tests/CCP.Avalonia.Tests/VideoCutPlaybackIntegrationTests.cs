using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Xunit;

namespace CCP.Avalonia.Tests;

public class VideoCutPlaybackIntegrationTests
{
    // Drive the real Show -> GuardTick -> grace/replay -> Scheduler.Stop -> CloseAll chain.
    // Only the decoder and time are fake. No fonts, window layout, native codecs or delays.
    [Fact]
    public Task Seek_buffering_grace_and_stop_credit_only_advancing_playback() => Run(10, (o, v, clock, players, credit) =>
    {
        var p = players.Single();
        o.GuardTick();
        Assert.Equal(60000, p.Time); // native seek command, fixed random draw
        o.GuardTick(); // baseline after seek
        Step(o, clock, p, 1000);
        clock.Advance(20000); // buffering: position stays put
        o.GuardTick();
        Assert.True(v.IsPlaying);
        Assert.True(o.TryGracePause(false));
        Assert.True(p.Paused);
        clock.Advance(60000);
        o.GuardTick();
        Assert.True(v.IsPlaying);
        o.ResumeFromGrace("test");
        Assert.False(p.Paused);
        o.GuardTick();
        Step(o, clock, p, 2000);
        v.Stop();
        Assert.Equal(3, credit()); // excludes seek, buffer and pause; includes the final stop sample
        Assert.True(p.Stopped && p.Disposed);
        Assert.Empty(o.Windows);
        v.Stop();
        Assert.Equal(3, credit()); // cleanup is idempotent
    });

    [Fact]
    public Task Replay_preserves_the_cap_and_watch_credit_and_stop_samples_the_tail() => Run(5, (o, v, clock, players, credit) =>
    {
        var first = players.Single();
        o.GuardTick();
        o.GuardTick();
        Step(o, clock, first, 2000);
        o.FrameTs = Stopwatch.GetTimestamp() - 10 * Stopwatch.Frequency; // deterministic output loss, not a real-time wait
        o.GuardTick();
        Assert.Equal(2, players.Count);
        Assert.True(first.Stopped && first.Disposed);
        Assert.Equal(2, credit()); // WPF credits at the heal teardown, before playback restarts
        var second = players.Last();
        o.GuardTick();
        Assert.Equal(60000, second.Time);
        o.GuardTick();
        Step(o, clock, second, 2000);
        Assert.True(v.IsPlaying); // 2 + 2 < the original 5 second allowance
        clock.Advance(1000);
        second.Time += 1000; // Stop must sample this, without a preceding guard tick
        v.Stop();
        Assert.Equal(5, credit());
        Assert.True(second.Stopped && second.Disposed);
    });

    [Fact]
    public Task Replay_cannot_restart_the_cap() => Run(5, (o, v, clock, players, credit) =>
    {
        var p = players.Single();
        o.GuardTick(); o.GuardTick();
        Step(o, clock, p, 2000);
        o.FrameTs = Stopwatch.GetTimestamp() - 10 * Stopwatch.Frequency;
        o.GuardTick();
        p = players.Last();
        o.GuardTick(); o.GuardTick();
        Step(o, clock, p, 3000);
        Assert.False(v.IsPlaying);
        Assert.Equal(5, credit());
        Assert.True(p.Stopped && p.Disposed);
    });

    private static void Step(MandatoryVideoOverlay o, Clock clock, Decoder p, int ms)
    {
        clock.Advance(ms);
        p.Time += ms;
        o.FrameTs = Stopwatch.GetTimestamp();
        o.GuardTick();
    }

    private static Task Run(int cap, Action<MandatoryVideoOverlay, MandatoryVideoScheduler, Clock, List<Decoder>, Func<double>> body)
        => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var s = CoreSettings.Current;
        var saved = (s.VideoMaxDurationSeconds, s.VideoCutRandomStart);
        var post = CoreDispatch.PostProvider;
        var track = CoreProgression.TrackVideoWatchedProvider;
        var clock = new Clock();
        var players = new List<Decoder>();
        var o = new MandatoryVideoOverlay(() => { var p = new Decoder(); players.Add(p); return p; })
            { Time = clock, CutRandom = new FixedRandom() };
        var v = new MandatoryVideoScheduler(o, clock, () => new[] { "/test/slice.mp4" });
        o.Scheduler = v;
        double credit = 0;
        try
        {
            (s.VideoMaxDurationSeconds, s.VideoCutRandomStart) = (cap, true);
            CoreDispatch.PostProvider = a => a();
            CoreProgression.TrackVideoWatchedProvider = n => credit += n;
            Assert.True(v.Trigger());
            clock.Fire(); // pre-roll, no sleeping
            Assert.Single(players);
            body(o, v, clock, players, () => credit);
        }
        finally
        {
            v.Stop();
            CoreDispatch.PostProvider = post;
            CoreProgression.TrackVideoWatchedProvider = track;
            (s.VideoMaxDurationSeconds, s.VideoCutRandomStart) = saved;
        }
        return Task.CompletedTask;
    });

    private sealed class FixedRandom : Random { public override long NextInt64(long max) => 60000; }
    private sealed class Decoder : IMandatoryPlayback
    {
        public long Time { get; set; }
        public long Length => 120000;
        public bool IsPlaying => !Paused && !Stopped;
        public bool IsSeekable => true;
        public bool HasVideo => true;
        public bool Paused, Stopped, Disposed;
        public void SetPause(bool paused) => Paused = paused;
        public void Stop() => Stopped = true;
        public void Dispose() => Disposed = true;
    }
    private sealed class Clock : TimeProvider
    {
        private long _ms;
        private ManualTimer? _timer;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => _ms;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddMilliseconds(_ms);
        public void Advance(int ms) => _ms += ms;
        public void Fire() => _timer!.Fire();
        public override ITimer CreateTimer(TimerCallback cb, object? state, TimeSpan due, TimeSpan period)
            => _timer = new ManualTimer(() => cb(state));
        private sealed class ManualTimer(Action fire) : ITimer
        {
            public void Fire() => fire();
            public bool Change(TimeSpan due, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => default;
        }
    }
}
