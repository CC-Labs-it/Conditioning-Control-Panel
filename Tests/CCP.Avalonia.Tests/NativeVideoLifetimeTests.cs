using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class NativeVideoLifetimeTests
{
    [Fact]
    public async Task Wedged_stop_is_bounded_quarantined_and_never_disposed_on_late_return()
    {
        var clock = new Clock();
        var safety = new NativeVideoSafety(clock);
        var lifetime = new NativeVideoLifetime(safety, () => { }, clock);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposed = 0; var freed = 0; var calls = 0;
        var graph = new object();
        try
        {
            var stop = lifetime.StopAsync(() =>
            {
                Interlocked.Increment(ref calls); entered.SetResult(); release.Wait(); exited.SetResult();
            }, () => disposed++, () => freed++, graph);
            Assert.Same(stop, lifetime.StopAsync(() => throw new Exception("second stop"), () => disposed++, () => freed++, graph));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(lifetime.Armed);
            clock.Advance(499);
            Assert.False(stop.IsCompleted);
            clock.Advance(1);
            await stop.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(lifetime.Armed);
            Assert.Equal(1, safety.QuarantinedCount);
            Assert.True(safety.CoolingDown);
            release.Set();
            await exited.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, calls);
            Assert.Equal(0, disposed);
            Assert.Equal(0, freed);
            clock.Advance(59999);
            Assert.True(safety.CoolingDown);
            clock.Advance(1);
            Assert.False(safety.CoolingDown);
            Assert.Equal(1, safety.QuarantinedCount); // cooldown expiry never recycles the graph
        }
        finally { release.Set(); }
    }

    [Fact]
    public async Task Successful_stop_orders_native_disposal_then_sink_free_and_completes_once()
    {
        var clock = new Clock();
        var safety = new NativeVideoSafety(clock);
        var lifetime = new NativeVideoLifetime(safety, () => throw new Exception("disarmed timer"), clock);
        var order = new List<string>();
        await lifetime.RunNative(() => order.Add("start"));
        await lifetime.StopAsync(() => order.Add("stop"), () => order.Add("dispose"),
            () => { Assert.True(lifetime.Armed); order.Add("free"); }, order);
        await lifetime.StopAsync(() => order.Add("again"), () => order.Add("again"), () => order.Add("again"), order);
        Assert.Equal(new[] { "start", "stop", "dispose", "free" }, order);
        Assert.False(lifetime.Armed);
        clock.Advance(9000);
        Assert.False(safety.CoolingDown);
        Assert.Equal(0, safety.QuarantinedCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Startup_and_managed_heartbeat_are_guarded_until_teardown(bool startupWedges)
    {
        var clock = new Clock();
        var safety = new NativeVideoSafety(clock);
        var expired = 0;
        var lifetime = new NativeVideoLifetime(safety, () => expired++, clock);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startup = lifetime.RunNative(() => { entered.SetResult(); if (startupWedges) release.Wait(); });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (!startupWedges)
            {
                await startup;
                clock.Advance(7000);
                lifetime.Heartbeat();
            }
            clock.Advance(7999);
            Assert.Equal(0, expired);
            clock.Advance(1);
            Assert.Equal(1, expired);
            Assert.True(lifetime.Expired);
            Assert.True(safety.CoolingDown);
            lifetime.Heartbeat(); // A late dispatcher must not undo the timeout.
            clock.Advance(8000);
            Assert.Equal(1, expired);
            var stops = 0;
            var stop = lifetime.StopAsync(() => stops++, () => { }, () => { }, lifetime);
            if (startupWedges)
            {
                clock.Advance(500);
                await stop.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(0, stops); // Stop cannot race a still-running constructor/Play.
                Assert.Equal(1, safety.QuarantinedCount);
            }
            release.Set();
            await startup.WaitAsync(TimeSpan.FromSeconds(5));
            await stop.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { release.Set(); }
    }

    [Fact]
    public Task Native_calls_run_off_dispatcher_but_sink_free_returns_to_it() =>
        CCP.Avalonia.Testing.AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var dispatcher = global::Avalonia.Threading.Dispatcher.UIThread;
            var lifetime = new NativeVideoLifetime(new NativeVideoSafety(TimeProvider.System), () => { });
            var startOnUi = true; var stopOnUi = true; var disposeOnUi = true; var freeOnUi = false;
            await lifetime.RunNative(() => startOnUi = dispatcher.CheckAccess());
            await lifetime.StopAsync(() => stopOnUi = dispatcher.CheckAccess(),
                () => disposeOnUi = dispatcher.CheckAccess(), () => freeOnUi = dispatcher.CheckAccess(), lifetime);
            Assert.False(startOnUi);
            Assert.False(stopOnUi);
            Assert.False(disposeOnUi);
            Assert.True(freeOnUi);
        });

    [Fact]
    public async Task Throwing_stop_is_quarantined_without_freeing_native_callback_memory()
    {
        var clock = new Clock();
        var safety = new NativeVideoSafety(clock);
        var lifetime = new NativeVideoLifetime(safety, () => { }, clock);
        var disposed = 0; var freed = 0;
        await lifetime.StopAsync(() => throw new InvalidOperationException(),
            () => disposed++, () => freed++, lifetime);
        Assert.Equal(0, disposed);
        Assert.Equal(0, freed);
        Assert.Equal(1, safety.QuarantinedCount);
        Assert.True(safety.CoolingDown);
        Assert.False(lifetime.Armed);
    }

    internal sealed class Clock : TimeProvider
    {
        private readonly List<Timer> _timers = new();
        private long _ms;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Interlocked.Read(ref _ms);
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddMilliseconds(GetTimestamp());
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Timer(this, callback, state);
            lock (_timers) { timer.Change(dueTime, period); _timers.Add(timer); }
            return timer;
        }
        internal void Advance(long milliseconds)
        {
            Interlocked.Add(ref _ms, milliseconds);
            Timer[] due;
            lock (_timers) due = _timers.Where(t => t.Due <= _ms).ToArray();
            foreach (var timer in due) timer.Fire();
        }
        private sealed class Timer(Clock clock, TimerCallback callback, object? state) : ITimer
        {
            internal long Due = long.MaxValue;
            private long _period;
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (clock._timers)
                {
                    Due = dueTime < TimeSpan.Zero ? long.MaxValue : clock.GetTimestamp() + (long)dueTime.TotalMilliseconds;
                    _period = period <= TimeSpan.Zero ? 0 : (long)period.TotalMilliseconds;
                    return true;
                }
            }
            internal void Fire()
            {
                lock (clock._timers)
                {
                    if (Due > clock.GetTimestamp()) return;
                    Due = _period == 0 ? long.MaxValue : Due + ((clock.GetTimestamp() - Due) / _period + 1) * _period;
                }
                callback(state);
            }
            public void Dispose() { lock (clock._timers) Due = long.MaxValue; }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
