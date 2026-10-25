using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using NAudio.Wave;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #1295: the order a waveOut player is released in. Stop first, Dispose only once the playback
/// thread has reported out, abandon (never dispose) a device that does not, and never tear the
/// same player down twice.
/// </summary>
public class WaveOutTeardownTests
{
    private sealed class FakePlayer : IWavePlayer
    {
        private readonly List<string> _calls = new();
        public string[] Calls { get { lock (_calls) return _calls.ToArray(); } }
        public Action? OnStop;
        public bool ReportedOut;
        public PlaybackState State = PlaybackState.Playing;

        public FakePlayer(PlaybackState state = PlaybackState.Playing)
        {
            State = state;
        }

        public event EventHandler<StoppedEventArgs>? PlaybackStopped;
        public PlaybackState PlaybackState => State;
        public float Volume { get; set; }
        public WaveFormat OutputWaveFormat => new(44100, 2);
        public void Init(IWaveProvider waveProvider) { }
        public void Play() => State = PlaybackState.Playing;
        public void Pause() => State = PlaybackState.Paused;

        public void Stop()
        {
            lock (_calls) _calls.Add("stop");
            State = PlaybackState.Stopped;
            OnStop?.Invoke();
        }

        public void ReportOut()
        {
            lock (_calls) _calls.Add("report");
            ReportedOut = true;
            PlaybackStopped?.Invoke(this, new StoppedEventArgs());
        }

        public void Dispose() { lock (_calls) _calls.Add("dispose"); }
    }

    private sealed class FakeSource : IDisposable
    {
        public int Disposed;
        public void Dispose() => Interlocked.Increment(ref Disposed);
    }

    [Fact]
    public void PlayingDevice_IsStoppedThenDisposed_AfterItReportsOut()
    {
        var player = new FakePlayer();
        var source = new FakeSource();

        bool waited = false;
        var outcome = WaveOutTeardown.Run(player, new IDisposable?[] { source }, waitMs: 2_000, graceMs: 10,
            wait: (reported, timeout) =>
            {
                waited = true;
                Assert.Equal(2_000, timeout);
                Assert.Equal(new[] { "stop" }, player.Calls);
                Assert.Equal(0, source.Disposed);
                Assert.False(reported.IsSet);
                // Deliver the real event at the wait boundary, with no thread-pool race.
                player.ReportOut();
                return reported.Wait(0);
            });

        Assert.True(waited);
        Assert.Equal(WaveOutTeardown.Outcome.Disposed, outcome);
        Assert.Equal(new[] { "stop", "report", "dispose" }, player.Calls);
        Assert.Equal(1, source.Disposed);
    }

    [Fact]
    public void DeviceThatNeverReportsOut_IsAbandoned_NotDisposed()
    {
        var player = new FakePlayer();
        var source = new FakeSource();

        bool waited = false;
        var outcome = WaveOutTeardown.Run(player, new IDisposable?[] { source }, waitMs: 100, graceMs: 10,
            wait: (reported, timeout) =>
            {
                waited = true;
                Assert.Equal(100, timeout);
                Assert.Equal(new[] { "stop" }, player.Calls);
                Assert.False(reported.IsSet);
                return reported.Wait(0); // deterministically expire the unsignalled wait
            });

        Assert.True(waited);
        Assert.Equal(WaveOutTeardown.Outcome.Abandoned, outcome);
        Assert.DoesNotContain("dispose", player.Calls);
        Assert.Equal(0, source.Disposed);   // the stuck thread may still be reading it
    }

    [Fact]
    public void AlreadyStoppedDevice_IsDisposedAfterTheGrace_WithoutWaitingForAnEvent()
    {
        var player = new FakePlayer(state: PlaybackState.Stopped);

        bool waited = false;
        var outcome = WaveOutTeardown.Run(player, Array.Empty<IDisposable?>(), waitMs: 5_000, graceMs: 10,
            wait: (reported, timeout) =>
            {
                waited = true;
                Assert.Equal(10, timeout); // grace, never the playing-device deadline
                Assert.Equal(new[] { "stop" }, player.Calls);
                Assert.False(reported.IsSet);
                return reported.Wait(0);
            });

        Assert.Equal(WaveOutTeardown.Outcome.Disposed, outcome);
        Assert.Contains("dispose", player.Calls);
        Assert.True(waited);
    }

    [Fact]
    public void SamePlayer_IsOnlyTornDownOnce()
    {
        var player = new FakePlayer();
        player.OnStop = player.ReportOut;

        var first = WaveOutTeardown.Run(player, Array.Empty<IDisposable?>(), waitMs: 2_000, graceMs: 10);
        var second = WaveOutTeardown.Run(player, Array.Empty<IDisposable?>(), waitMs: 2_000, graceMs: 10);

        Assert.Equal(WaveOutTeardown.Outcome.Disposed, first);
        Assert.Equal(WaveOutTeardown.Outcome.Duplicate, second);
        Assert.Equal(new[] { "stop", "report", "dispose" }, player.Calls);
    }

    [Fact]
    public void SourcesOnly_AreDisposed()
    {
        var a = new FakeSource();
        var b = new FakeSource();

        var outcome = WaveOutTeardown.Run(null, new IDisposable?[] { a, null, b }, waitMs: 100, graceMs: 10);

        Assert.Equal(WaveOutTeardown.Outcome.Disposed, outcome);
        Assert.Equal(1, a.Disposed);
        Assert.Equal(1, b.Disposed);
    }

    [Fact]
    public async Task Release_ReturnsAtOnce_BeforeTheDeviceReportsOut()
    {
        var player = new FakePlayer();
        var source = new FakeSource();
        using var allowStopToReturn = new ManualResetEventSlim(false);
        var stopEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread? teardownThread = null;
        Thread? callerThread = null;
        player.OnStop = () =>
        {
            teardownThread = Thread.CurrentThread;
            stopEntered.SetResult();
            allowStopToReturn.Wait();
        };

        // Dedicated caller: neither its start nor the fake device depends on pool availability.
        var caller = Task.Factory.StartNew(() =>
        {
            callerThread = Thread.CurrentThread;
            WaveOutTeardown.Release(player, source, "test");
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        // Watchdog only, not a latency assertion. The closed gate is the proof: Release must
        // return while Stop is still blocked and PlaybackStopped has NEVER been signalled.
        var watchdog = TimeSpan.FromSeconds(10);
        try
        {
            await Task.WhenAll(caller, stopEntered.Task).WaitAsync(watchdog);
            Assert.NotSame(callerThread, teardownThread);
            Assert.False(allowStopToReturn.IsSet);
            Assert.False(player.ReportedOut);
            Assert.Equal(new[] { "stop" }, player.Calls);
            Assert.Equal(0, source.Disposed);
        }
        finally
        {
            // Report out only after Release has returned, so the teardown finishes at once
            // instead of sitting out the real 2,000 ms deadline (no real-time wait in the test).
            player.ReportOut();
            allowStopToReturn.Set();
            await caller.WaitAsync(watchdog);
            await stopEntered.Task.WaitAsync(watchdog);
            // Observe the actual teardown exit; do not leave a background thread for the next test.
            if (teardownThread != null && teardownThread != callerThread)
                await Task.Factory.StartNew(() => teardownThread.Join(), CancellationToken.None,
                    TaskCreationOptions.LongRunning, TaskScheduler.Default).WaitAsync(watchdog);
        }

        Assert.Equal(new[] { "stop", "report", "dispose" }, player.Calls);
        Assert.Equal(1, source.Disposed);
    }

    [Fact]
    public void NewWaveOut_CapturesNoContext_AndRestoresTheCallersOne()
    {
        // A player built on the UI thread would POST PlaybackStopped to the dispatcher, and the
        // teardown's report-out wait would then hang on a busy UI thread.
        var prev = SynchronizationContext.Current;
        var ctx = new SynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(ctx);
        try
        {
            var w = WaveOutTeardown.NewWaveOut();
            try
            {
                Assert.Same(ctx, SynchronizationContext.Current);
                var field = typeof(WaveOutEvent).GetField("syncContext",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.NotNull(field);   // NAudio 2.2.1 keeps it here; a rename would silently void this fix
                Assert.Null(field!.GetValue(w));
            }
            finally { w.Dispose(); }
        }
        finally { SynchronizationContext.SetSynchronizationContext(prev); }
    }
}
