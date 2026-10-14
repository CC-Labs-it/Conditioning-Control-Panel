using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>No desktop audio: a stepped 2.5-second timer and an in-memory pactl transport.</summary>
public sealed class AudioDuckRescanTests
{
    private static string Streams(params (int Id, int Volume, int Pid)[] streams) =>
        System.Text.Json.JsonSerializer.Serialize(streams.Select(s => new
        {
            index = s.Id, volume = new { left = new { value = s.Volume } },
            properties = new Dictionary<string, string> { ["application.process.id"] = s.Pid.ToString() },
        }));

    [Fact]
    public void LateStreamIsDuckedAtTwoPointFiveSeconds_OriginalsAreNeverCompounded()
    {
        var clock = new Clock();
        var sets = new List<string>();
        var scans = 0;
        var streams = Streams((7, 65536, 1));
        var audio = new LibVlcAudio(args =>
        {
            if (args.StartsWith("-f json")) { Interlocked.Increment(ref scans); return streams; }
            sets.Add(args);
            return "";
        }, clock);
        try
        {
            audio.Duck(50);
            audio.Drain();
            streams = Streams((7, 32768, 1), (8, 40000, 2), (9, 65536, Environment.ProcessId), (10, 0, 3), (11, 65, 4));
            clock.Advance(TimeSpan.FromMilliseconds(2499));
            audio.Drain();
            Assert.Single(sets);
            clock.Advance(TimeSpan.FromMilliseconds(1));
            audio.Drain();
            Assert.Equal(new[] { "set-sink-input-volume 7 32768", "set-sink-input-volume 8 20000" }, sets);
            clock.Advance(TimeSpan.FromMilliseconds(2500));
            audio.Drain();
            Assert.Equal(2, sets.Count);
            var retired = Assert.Single(clock.Active);
            audio.Unduck(-1);
            audio.Drain();
            Assert.Contains("set-sink-input-volume 7 65536", sets);
            Assert.Contains("set-sink-input-volume 8 40000", sets);
            var restored = sets.Count;
            clock.Advance(TimeSpan.FromSeconds(10));
            audio.Drain();
            Assert.Equal(restored, sets.Count);
            Assert.Empty(clock.Active);
            audio.Duck(80);
            audio.Drain();
            var beforeStaleCallback = scans;
            retired.Fire(); // ordinary unduck did not advance the public duck generation
            audio.Drain();
            Assert.Equal(beforeStaleCallback, scans);
        }
        finally { audio.Shutdown(); }
    }

    [Fact]
    public void SlowSweepDoesNotAccumulateTimerWork_ForceUnduckAndExitRetireTheTimer()
    {
        var clock = new Clock();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var cancellation = TestContext.Current.CancellationToken;
        var enumerations = 0;
        var audio = new LibVlcAudio(args =>
        {
            if (!args.StartsWith("-f json")) return "";
            if (Interlocked.Increment(ref enumerations) == 1)
            {
                entered.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(10), cancellation));
            }
            return Streams((7, 65536, 1));
        }, clock);
        try
        {
            audio.Duck(80);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), cancellation));
            Assert.Single(clock.Active);
            clock.Advance(TimeSpan.FromSeconds(20));
            release.Set();
            audio.Drain();
            Assert.Equal(1, enumerations);
            clock.Advance(TimeSpan.FromMilliseconds(2500));
            audio.Drain();
            Assert.Equal(2, enumerations);
            var retired = Assert.Single(clock.Active);
            audio.ForceUnduck();
            audio.Drain();
            Assert.Empty(clock.Active);
            clock.Advance(TimeSpan.FromSeconds(10));
            Assert.Equal(2, enumerations);
            audio.Duck(80);
            audio.Drain();
            Assert.Single(clock.Active);
            var beforeStaleCallback = enumerations;
            retired.Fire(); // a callback already dispatched when its timer was disposed
            audio.Drain();
            Assert.Equal(beforeStaleCallback, enumerations);
            audio.Shutdown();
            Assert.Empty(clock.Active);
        }
        finally { release.Set(); audio.Shutdown(); }
    }

    private sealed class Clock : TimeProvider
    {
        private TimeSpan _now;
        private readonly List<Timer> _timers = new();
        public IEnumerable<Timer> Active => _timers.Where(t => !t.Disposed);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan due, TimeSpan period)
        {
            var timer = new Timer(this, () => callback(state), period);
            _timers.Add(timer);
            timer.Change(due, period);
            return timer;
        }
        public void Advance(TimeSpan by)
        {
            var end = _now + by;
            while (Active.Where(t => t.Due <= end).OrderBy(t => t.Due).FirstOrDefault() is { } next)
            {
                _now = next.Due;
                next.Due += next.Period;
                next.Fire();
            }
            _now = end;
        }
        public sealed class Timer(Clock clock, Action fire, TimeSpan period) : ITimer
        {
            public bool Disposed;
            public TimeSpan Due, Period = period;
            public void Fire() => fire();
            public bool Change(TimeSpan due, TimeSpan repeat) { Due = clock._now + due; Period = repeat; return true; }
            public void Dispose() => Disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return default; }
        }
    }
}
