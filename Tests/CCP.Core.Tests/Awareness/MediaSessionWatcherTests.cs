using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Awareness;
using Xunit;

namespace CCP.Core.Tests.Awareness;

public sealed class MediaSessionWatcherTests
{
    private sealed class Clock : TimeProvider
    {
        public readonly List<Timer> Timers = new();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Timer(callback, state);
            timer.Change(dueTime, period);
            Timers.Add(timer);
            return timer;
        }
    }

    private sealed class Timer(TimerCallback callback, object? state) : ITimer
    {
        public TimeSpan Due, Period;
        public bool Disposed;
        public bool ThrowWhenDisposed;
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (Disposed)
            {
                if (ThrowWhenDisposed) throw new ObjectDisposedException(nameof(Timer));
                return false;
            }
            Due = dueTime; Period = period;
            return true;
        }
        public void Fire() { if (!Disposed && Due != Timeout.InfiniteTimeSpan) callback(state); }
        public void Dispose() => Disposed = true;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }

    private static MediaSample Track(string title = "fixture") => new(title, "artist", "Playing", TimeSpan.FromSeconds(12));
    private static TaskCompletionSource<T> Pending<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static Task FinishStart(MediaSessionWatcher watcher) => watcher.StartCompletion.WaitAsync(TimeSpan.FromSeconds(5));

    [Fact]
    public async Task StartPollsAtThreeSecondsAndResumeReusesAcquisitionAndTimer()
    {
        var clock = new Clock();
        int acquisitions = 0, reads = 0;
        using var watcher = new MediaSessionWatcher(() => { acquisitions++; return Task.FromResult(true); },
            () => { reads++; return Task.FromResult<MediaSample?>(Track()); }, clock);
        Assert.Empty(clock.Timers); // construction observes nothing
        watcher.Start(); watcher.Start();
        await FinishStart(watcher);
        Assert.Equal(1, acquisitions);
        var timer = Assert.Single(clock.Timers);
        Assert.Equal(TimeSpan.Zero, timer.Due);
        Assert.Equal(TimeSpan.FromSeconds(3), timer.Period);
        timer.Fire();
        Assert.Equal(1, reads);
        Assert.Equal(Track(), watcher.Current);
        watcher.Stop();
        Assert.Null(watcher.Current);
        Assert.True(watcher.IsAvailable); // availability means the manager was obtained
        Assert.Equal(Timeout.InfiniteTimeSpan, timer.Due);
        timer.Fire();
        Assert.Equal(1, reads);
        watcher.Start();
        await FinishStart(watcher);
        Assert.Equal(1, acquisitions);
        Assert.Single(clock.Timers);
        timer.Fire();
        Assert.Equal(2, reads);
    }

    [Fact]
    public async Task PauseDuringAcquisitionArmsNothingAndResumeSharesPendingManager()
    {
        var ready = Pending<bool>();
        var clock = new Clock();
        int acquisitions = 0;
        using var watcher = new MediaSessionWatcher(() => { acquisitions++; return ready.Task; },
            () => Task.FromResult<MediaSample?>(null), clock);
        watcher.Start();
        var firstStart = watcher.StartCompletion;
        watcher.Stop();
        ready.SetResult(true);
        await firstStart.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(clock.Timers);
        Assert.False(watcher.IsAvailable);
        watcher.Start();
        await FinishStart(watcher);
        Assert.Single(clock.Timers);
        Assert.Equal(1, acquisitions);
    }

    [Fact]
    public async Task RapidStopStartDuringAcquisitionRequestsOnlyOneManager()
    {
        var ready = Pending<bool>();
        var clock = new Clock();
        int acquisitions = 0;
        using var watcher = new MediaSessionWatcher(() => { acquisitions++; return ready.Task; },
            () => Task.FromResult<MediaSample?>(null), clock);
        watcher.Start(); watcher.Stop(); watcher.Start();
        ready.SetResult(true);
        await FinishStart(watcher);
        Assert.Equal(1, acquisitions);
        Assert.Single(clock.Timers);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnavailableAcquisitionIsQuietAndCanRetryAfterPause(bool throws)
    {
        var clock = new Clock();
        int acquisitions = 0;
        using var watcher = new MediaSessionWatcher(() => ++acquisitions == 1
            ? throws ? Task.FromException<bool>(new InvalidOperationException("denied")) : Task.FromResult(false)
            : Task.FromResult(true), () => Task.FromResult<MediaSample?>(null), clock);
        watcher.Start();
        await FinishStart(watcher);
        Assert.False(watcher.IsAvailable);
        Assert.Empty(clock.Timers);
        watcher.Start();
        Assert.Equal(1, acquisitions);
        watcher.Stop(); watcher.Start();
        await FinishStart(watcher);
        Assert.True(watcher.IsAvailable);
        Assert.Equal(2, acquisitions);
        Assert.Single(clock.Timers);
    }

    [Fact]
    public async Task DisposalDuringAcquisitionNeverArmsAPoll()
    {
        var ready = Pending<bool>();
        var clock = new Clock();
        var watcher = new MediaSessionWatcher(() => ready.Task,
            () => Task.FromResult<MediaSample?>(Track()), clock);
        watcher.Start();
        watcher.Dispose();
        ready.SetResult(true);
        await FinishStart(watcher);
        Assert.Empty(clock.Timers);
        Assert.False(watcher.IsAvailable);
        Assert.Null(watcher.Current);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AReadFinishingAfterPauseCannotPublishEvenAfterRestart(bool restart)
    {
        var pending = Pending<MediaSample?>();
        int reads = 0;
        using var watcher = new MediaSessionWatcher(() => Task.FromResult(true),
            () => { reads++; return reads == 1 ? pending.Task : Task.FromResult<MediaSample?>(Track("new")); }, new Clock());
        watcher.Start();
        await FinishStart(watcher);
        var read = watcher.PollAsync();
        await watcher.PollAsync();
        Assert.Equal(1, reads); // no overlapping reads, even across timer ticks
        watcher.Stop();
        if (restart) { watcher.Start(); await FinishStart(watcher); }
        pending.SetResult(Track("old"));
        await read;
        Assert.Null(watcher.Current);
        if (restart)
        {
            await watcher.PollAsync();
            Assert.Equal(Track("new"), watcher.Current);
        }
    }

    [Fact]
    public async Task DisposeClearsCurrentStopsTimerAndDiscardsPendingReads()
    {
        var pending = Pending<MediaSample?>();
        var clock = new Clock();
        int reads = 0;
        var watcher = new MediaSessionWatcher(() => Task.FromResult(true),
            () => ++reads == 1 ? Task.FromResult<MediaSample?>(Track()) : pending.Task, clock);
        watcher.Start();
        await FinishStart(watcher);
        await watcher.PollAsync();
        Assert.NotNull(watcher.Current);
        var read = watcher.PollAsync();
        watcher.Dispose(); watcher.Dispose(); watcher.Start();
        pending.SetResult(Track("late"));
        await read;
        Assert.Null(watcher.Current);
        Assert.False(watcher.IsAvailable);
        Assert.True(Assert.Single(clock.Timers).Disposed);
        await watcher.PollAsync();
        Assert.Equal(2, reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ADisposedTimerIsRecreatedOnResume(bool throws)
    {
        var clock = new Clock();
        using var watcher = new MediaSessionWatcher(() => Task.FromResult(true),
            () => Task.FromResult<MediaSample?>(null), clock);
        watcher.Start(); await FinishStart(watcher);
        watcher.Stop();
        var timer = Assert.Single(clock.Timers);
        timer.ThrowWhenDisposed = throws;
        timer.Dispose();
        watcher.Start(); await FinishStart(watcher);
        Assert.Equal(2, clock.Timers.Count);
        Assert.False(clock.Timers[1].Disposed);
    }

    [Fact]
    public async Task FailedReadKeepsLastSampleAndNoSessionClearsIt()
    {
        int reads = 0;
        using var watcher = new MediaSessionWatcher(() => Task.FromResult(true), () => ++reads switch
        {
            1 => Task.FromResult<MediaSample?>(Track()),
            2 => Task.FromException<MediaSample?>(new InvalidOperationException("session vanished")),
            _ => Task.FromResult<MediaSample?>(null),
        }, new Clock());
        watcher.Start(); await FinishStart(watcher);
        await watcher.PollAsync(); await watcher.PollAsync();
        Assert.Equal(Track(), watcher.Current);
        await watcher.PollAsync();
        Assert.Null(watcher.Current);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void BlankTitlesAreNotUsableSessions(string? title) =>
        Assert.Null(MediaSample.FromSession(title, "artist", "Playing", TimeSpan.Zero));

    [Fact]
    public void SessionNormalizationPreservesTitleAndPositionAndUsesUnknownState()
    {
        var sample = MediaSample.FromSession("  title  ", " ", null, TimeSpan.FromSeconds(42));
        Assert.Equal(new MediaSample("  title  ", null, "Unknown", TimeSpan.FromSeconds(42)), sample);
        Assert.False(sample!.IsPlaying);
        Assert.True(MediaSample.FromSession("title", "artist", "playing", TimeSpan.Zero)!.IsPlaying);
    }
    [Fact]
    public async Task FrameMediaCountsStrictRewindsAndKeepsChangePendingUntilConsumed()
    {
        MediaSample? sample = Track("A") with { Position = TimeSpan.FromSeconds(30) };
        using var watcher = new MediaSessionWatcher(() => Task.FromResult(true),
            () => Task.FromResult(sample), new Clock());
        watcher.Start(); await FinishStart(watcher);
        async Task<MediaInfo?> Tick(string title, int seconds, string state = "Playing")
        {
            sample = new MediaSample(title, "artist", state, TimeSpan.FromSeconds(seconds));
            await watcher.PollAsync();
            return watcher.TrackMedia();
        }
        Assert.Equal(new MediaInfo("A", "artist", "Playing", 1), await Tick("A", 30));
        Assert.False(watcher.TakeMediaChange()); // first track is not a change
        Assert.Equal(1, (await Tick("A", 20))!.RepeatCount); // exactly 10s is not a rewind
        Assert.Equal(2, (await Tick("A", 0))!.RepeatCount);
        Assert.False(watcher.TakeMediaChange()); // second play is below the loop threshold
        Assert.Equal(2, (await Tick("A", 30, "Paused"))!.RepeatCount);
        Assert.Equal(3, (await Tick("A", 0))!.RepeatCount);
        Assert.Equal(3, watcher.TrackMedia()!.RepeatCount); // observer faster than backend
        Assert.True(watcher.TakeMediaChange());
        Assert.False(watcher.TakeMediaChange());
        Assert.Equal(new MediaInfo("B", "artist", "Paused", 1), await Tick("B", 0, "Paused"));
        await Tick("B", 1); // another tick must not consume the pending candidate
        Assert.True(watcher.TakeMediaChange());
        Assert.Equal(1, (await Tick("A", 0))!.RepeatCount); // A/B/A does not accumulate
    }

    [Fact]
    public async Task MissingSessionResetsRepeatIdentityButPreservesPendingCandidateLikeWpf()
    {
        MediaSample? sample = Track("A");
        using var watcher = new MediaSessionWatcher(() => Task.FromResult(true),
            () => Task.FromResult(sample), new Clock());
        watcher.Start(); await FinishStart(watcher);
        await watcher.PollAsync(); watcher.TrackMedia();
        sample = Track("B"); await watcher.PollAsync(); watcher.TrackMedia();
        sample = null; await watcher.PollAsync();
        Assert.Null(watcher.TrackMedia());
        Assert.True(watcher.TakeMediaChange());
        sample = Track("B"); await watcher.PollAsync();
        Assert.Equal(1, watcher.TrackMedia()!.RepeatCount);
        Assert.False(watcher.TakeMediaChange());
    }

    [Theory]
    [InlineData("wipe")]
    [InlineData("stop")]
    [InlineData("dispose")]
    public async Task MediaErasureDropsPendingChangesAndLoopHistory(string action)
    {
        MediaSample? sample = Track("A");
        using var watcher = new MediaSessionWatcher(() => Task.FromResult(true),
            () => Task.FromResult(sample), new Clock());
        watcher.Start(); await FinishStart(watcher);
        await watcher.PollAsync(); watcher.TrackMedia();
        sample = Track("B"); await watcher.PollAsync(); watcher.TrackMedia();
        if (action == "wipe") watcher.ResetMediaState();
        else if (action == "stop") watcher.Stop();
        else watcher.Dispose();
        Assert.False(watcher.TakeMediaChange());
        if (action == "wipe")
            Assert.Equal(1, watcher.TrackMedia()!.RepeatCount);
        else
            Assert.Null(watcher.TrackMedia());
    }

}
