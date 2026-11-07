using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.GoonGame;
using Serilog;
using Serilog.Events;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// The Goon Game clock and sudden-death round rules after the Core move (goongame-core-c): the
/// NTP-style sync converges, scheduling refuses an unsynced clock, and every UI hop the clock and
/// the rounds make goes through the <see cref="CoreDispatch"/> seam instead of a WPF dispatcher.
/// </summary>
public sealed class GoonClockAndRoundsTests : IDisposable
{
    private readonly Action<Action>? _savedPost = CoreDispatch.PostProvider;
    private readonly Func<Func<object?>, TimeSpan, (bool, object?)>? _savedInvoke = CoreDispatch.InvokeProvider;

    public void Dispose()
    {
        CoreDispatch.PostProvider = _savedPost;
        CoreDispatch.InvokeProvider = _savedInvoke;
    }

    private static (MatchClock Host, MatchClock Guest) WiredPair(long guestSkewMs, TimeProvider? time = null)
    {
        var host = new MatchClock(isClockMaster: true, "test:host", time: time);
        var guest = new MatchClock(isClockMaster: false, "test:guest", guestSkewMs, time);
        host.Attach((m, _) => { guest.TryHandleMessage(m); return Task.CompletedTask; });
        guest.Attach((m, _) => { host.TryHandleMessage(m); return Task.CompletedTask; });
        return (host, guest);
    }

    [Fact]
    public async Task GuestClockConvergesOnTheHostsSkewAndHostStaysPinnedAtZero()
    {
        var (host, guest) = WiredPair(guestSkewMs: 5000);
        using (host) using (guest)
        {
            Assert.True(await host.SyncAsync(CancellationToken.None));
            Assert.True(await guest.SyncAsync(CancellationToken.None));

            Assert.Equal(0, host.OffsetMs);
            // The guest's local clock runs 5 s ahead, so it must subtract ~5 s to reach match time.
            Assert.InRange(guest.OffsetMs, -5050, -4950);
            Assert.InRange(Math.Abs(guest.NowMatchMs - host.NowMatchMs), 0, 50);
        }
    }

    [Fact]
    public void ScheduleAtRefusesAnUnsyncedClock()
    {
        using var clock = new MatchClock(isClockMaster: true);
        var fired = false;
        Assert.Null(clock.ScheduleAt(clock.NowMatchMs + 5000, () => fired = true, "unsynced"));
        Assert.False(fired);
    }

    [Fact]
    public async Task SyncedEventAndScheduledFireBothHopThroughCoreDispatch()
    {
        var ct = TestContext.Current.CancellationToken;
        var posted = Channel.CreateUnbounded<Action>();
        var hops = 0;
        CoreDispatch.PostProvider = a => { Interlocked.Increment(ref hops); posted.Writer.TryWrite(a); };

        // Advancing the PopQuiz fake across several delay slices races RunAsync's continuation:
        // it can read the old time, then arm its next relative delay after Advance has moved on.
        // Wait for each timer to be armed before moving time; complete it on a worker like a real timer.
        var clock = new ScheduledClock();
        var (host, guest) = WiredPair(guestSkewMs: 0, clock);
        using (host) using (guest)
        {
            var synced = 0;
            host.Synced += (_, _) => synced++;
            Assert.True(await host.SyncAsync(ct));
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(5)); // same total fire budget as the original test
            var waitToken = deadline.Token;
            Assert.Equal(0, synced);                       // the dispatcher still owns the callback
            (await posted.Reader.ReadAsync(waitToken))();
            Assert.Equal(1, synced);
            Assert.Equal(1, Volatile.Read(ref hops));

            var fires = 0;
            var at = host.SafeFireAt();
            var target = TimeSpan.FromMilliseconds(at - host.NowMatchMs).Ticks;
            using var handle = host.ScheduleAt(at, () => fires++, "fire");
            Assert.NotNull(handle);
            // Keep the exact buffer boundary: do not hide rejection bugs with extra lead.
            Assert.Null(host.ScheduleAt(at - 1, () => fires++, "too soon"));

            var delay = await clock.NextDelayAsync(waitToken);
            while (delay.Due < target)
            {
                clock.AdvanceTo(delay.Due);
                await Task.Run(delay.Fire, waitToken);
                Assert.False(posted.Reader.TryRead(out _));
                delay = await clock.NextDelayAsync(waitToken);
            }

            Assert.Equal(target, delay.Due);               // not a millisecond early
            Assert.Equal(1, Volatile.Read(ref hops));
            clock.AdvanceTo(target);
            await Task.Run(delay.Fire, waitToken);
            var fire = await posted.Reader.ReadAsync(waitToken);
            Assert.Equal(0, fires);                        // scheduling must not bypass the dispatcher
            Assert.Equal(2, Volatile.Read(ref hops));
            fire();
            Assert.Equal(1, fires);
            Assert.False(posted.Reader.TryRead(out _));
        }
    }

    // This test only needs one-shot Task.Delay timers. Publishing each armed timer lets
    // the driver rendezvous with background continuations instead of guessing when they have run.
    private sealed class ScheduledClock : TimeProvider
    {
        private long _ticks;
        private readonly Channel<PendingDelay> _delays = Channel.CreateUnbounded<PendingDelay>();
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _ticks);
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(GetTimestamp());

        public void AdvanceTo(long ticks)
        {
            Assert.True(ticks >= GetTimestamp(), "Test time must remain monotonic");
            Interlocked.Exchange(ref _ticks, ticks);
        }

        public ValueTask<PendingDelay> NextDelayAsync(CancellationToken ct) => _delays.Reader.ReadAsync(ct);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Assert.Equal(Timeout.InfiniteTimeSpan, period);
            Assert.True(dueTime > TimeSpan.Zero);
            var delay = new PendingDelay(callback, state, GetTimestamp() + dueTime.Ticks);
            _delays.Writer.TryWrite(delay);
            return delay;
        }

        public sealed class PendingDelay(TimerCallback callback, object? state, long due) : ITimer
        {
            private int _disposed;
            public long Due { get; } = due;
            public void Fire() { if (Volatile.Read(ref _disposed) == 0) callback(state); }
            public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException("One-shot delay only");
            public void Dispose() => Interlocked.Exchange(ref _disposed, 1);
            public ValueTask DisposeAsync() { Dispose(); return default; }
        }
    }

    [Fact]
    public void DispatchedCallbackThatThrowsIsContainedInsteadOfReachingTheHead()
    {
        Action? queued = null;
        CoreDispatch.PostProvider = a => queued = a;

        GoonDispatch.Post(() => throw new InvalidOperationException("handler bug"));

        Assert.NotNull(queued);
        queued!();   // what the head's dispatcher would run: must not throw
    }

    [Fact]
    public void PresenterCallsRunInlineHeadlessAndSwallowFaults()
    {
        CoreDispatch.InvokeProvider = null;
        var ran = false;
        GoonUi.Invoke(() => ran = true, "inline");
        Assert.True(ran);

        GoonUi.Invoke(() => throw new InvalidOperationException("broken render"), "throws");
    }

    [Fact]
    public void PresenterCallsGoThroughTheBoundedUiHop()
    {
        TimeSpan? timeout = null;
        CoreDispatch.InvokeProvider = (f, t) => { timeout = t; return (true, f()); };
        var ran = false;
        GoonUi.Invoke(() => ran = true, "hop");
        Assert.True(ran);
        Assert.Equal(TimeSpan.FromSeconds(5), timeout);

        // A wedged UI thread: the hop gives up and the round carries on.
        ran = false;
        CoreDispatch.InvokeProvider = (_, _) => (false, null);
        var sink = new ListSink();
        var previous = Log.Logger;
        Log.Logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        try { GoonUi.Invoke(() => ran = true, "wedged"); }
        finally { Log.Logger = previous; }
        Assert.False(ran);
        // The bug-report marker rescue keys on [WATCHDOG], as DispatcherHelper.LogSyncTimeout did.
        Assert.Contains(sink.Events, e => e.MessageTemplate.Text.StartsWith("[WATCHDOG] "));
    }

    private sealed class ListSink : Serilog.Core.ILogEventSink
    {
        public List<LogEvent> Events { get; } = new();
        public void Emit(LogEvent logEvent) { lock (Events) Events.Add(logEvent); }
    }

    [Theory]
    [InlineData(GoonRoundKind.QuickDrawLockCard, true, 900, 0, null, true, 1200, 0, null, GoonRoundVerdict.Win)]
    [InlineData(GoonRoundKind.QuickDrawLockCard, false, 0, 0, null, true, 9000, 0, null, GoonRoundVerdict.Loss)]
    [InlineData(GoonRoundKind.StaringContest, false, 4000, 0, null, false, 3000, 0, null, GoonRoundVerdict.Win)]
    [InlineData(GoonRoundKind.ReactionDuel, true, 0, 0, 250, true, 0, 0, 180, GoonRoundVerdict.Loss)]
    [InlineData(GoonRoundKind.ReactionDuel, true, 0, 0, null, false, 0, 0, null, GoonRoundVerdict.Draw)]
    [InlineData(GoonRoundKind.BubbleRace, false, 0, 7, null, false, 0, 5, null, GoonRoundVerdict.Win)]
    public void JudgeDecidesEachRoundKindAndMirrorsForThePeer(
        GoonRoundKind kind,
        bool mineDone, int mineElapsed, int mineProgress, int? mineReaction,
        bool theirsDone, int theirsElapsed, int theirsProgress, int? theirsReaction,
        GoonRoundVerdict expected)
    {
        var mine = new RoundResultMsg { Completed = mineDone, ElapsedMs = mineElapsed, Progress = mineProgress, ReactionMs = mineReaction };
        var theirs = new RoundResultMsg { Completed = theirsDone, ElapsedMs = theirsElapsed, Progress = theirsProgress, ReactionMs = theirsReaction };

        Assert.Equal(expected, GoonRoundJudge.Decide(kind, mine, theirs));

        // Both machines run the same function with the arguments swapped; they must agree.
        var mirrored = expected switch
        {
            GoonRoundVerdict.Win => GoonRoundVerdict.Loss,
            GoonRoundVerdict.Loss => GoonRoundVerdict.Win,
            _ => GoonRoundVerdict.Draw,
        };
        Assert.Equal(mirrored, GoonRoundJudge.Decide(kind, theirs, mine));
    }
}
