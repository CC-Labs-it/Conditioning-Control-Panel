using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class ScreenOcrScanLoopTests
{
    private sealed class Clock : TimeProvider
    {
        internal readonly List<Timer> Timers = new();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Timer(callback, state, dueTime, period);
            lock (Timers) Timers.Add(timer);
            return timer;
        }
        internal int Count { get { lock (Timers) return Timers.Count; } }
        internal Timer Last { get { lock (Timers) return Timers.Last(); } }
    }
    private sealed class Timer(TimerCallback callback, object? state, TimeSpan due, TimeSpan period) : ITimer
    {
        internal TimeSpan Due = due, Period = period;
        internal bool Disposed;
        internal int Changes;
        public bool Change(TimeSpan next, TimeSpan repeat)
        { Due = next; Period = repeat; Changes++; return true; }
        internal void Fire() => callback(state); // can also simulate an already queued callback after disposal
        public void Dispose() => Disposed = true;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }

    [Fact]
    public void StartRetimesOnlyLiveTimerAndStopDisposeAreFinal()
    {
        var clock = new Clock();
        var scans = 0;
        using var loop = new ScreenOcrScanLoop<int>(_ => Task.FromResult(++scans),
            (_, _) => Task.CompletedTask, () => false, clock);
        loop.UpdateInterval(5000);
        Assert.Empty(clock.Timers);
        loop.Start(3000);
        var timer = Assert.Single(clock.Timers);
        Assert.Equal(TimeSpan.FromSeconds(3), timer.Due);
        Assert.Equal(timer.Due, timer.Period);
        Assert.Equal(0, scans); // no eager scan
        loop.Start(9000);
        Assert.Single(clock.Timers);
        Assert.Equal(TimeSpan.FromSeconds(3), timer.Due);
        loop.UpdateInterval(7000);
        Assert.Equal(TimeSpan.FromSeconds(7), timer.Due);
        Assert.Equal(timer.Due, timer.Period);
        Assert.Equal(1, timer.Changes);
        loop.Stop();
        Assert.True(timer.Disposed);
        loop.UpdateInterval(8000);
        timer.Fire();
        Assert.Equal(1, timer.Changes);
        Assert.Equal(0, scans);
        loop.Start(4000);
        Assert.Equal(2, clock.Timers.Count);
        loop.Dispose();
        loop.Start(2000);
        Assert.Equal(2, clock.Timers.Count);
        Assert.True(clock.Last.Disposed);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 4)]
    public async Task ConfirmationScansWait200msAndStopAtCap(bool pending, int expected)
    {
        var clock = new Clock();
        var scans = 0;
        var dispatched = new List<int>();
        using var loop = new ScreenOcrScanLoop<int>(_ => Task.FromResult(++scans),
            (n, _) => { dispatched.Add(n); return Task.CompletedTask; }, () => pending, clock);
        loop.Start(3000);
        var tick = loop.ScanTickAsync();
        for (int i = 1; i < expected; i++)
        {
            Assert.True(SpinWait.SpinUntil(() => clock.Count == i + 1, TimeSpan.FromSeconds(5)));
            Assert.Equal(i, scans);
            var delay = clock.Last;
            Assert.Equal(TimeSpan.FromMilliseconds(200), delay.Due);
            delay.Fire();
        }
        await tick.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(expected, scans);
        Assert.Equal(Enumerable.Range(1, expected), dispatched);
        Assert.Equal(expected, clock.Timers.Count); // no fourth quick scan scheduled
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InFlightScansDoNotOverlapOrDispatchAfterStop(bool dispose)
    {
        var clock = new Clock();
        var capture = new TaskCompletionSource<int>();
        var scans = 0;
        var dispatched = 0;
        using var loop = new ScreenOcrScanLoop<int>(_ =>
        {
            scans++;
            return capture.Task;
        }, (_, _) => { dispatched++; return Task.CompletedTask; }, () => false, clock);
        loop.Start(3000);
        var timer = clock.Last;
        var tick = loop.ScanTickAsync();
        await loop.ScanTickAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, scans);
        if (dispose) loop.Dispose(); else loop.Stop();
        capture.SetResult(42);
        await tick.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, dispatched);
        timer.Fire();
        Assert.Equal(1, scans);
    }

    [Fact]
    public async Task StopCancelsPendingConfirmationDelay()
    {
        var clock = new Clock();
        var scans = 0;
        using var loop = new ScreenOcrScanLoop<int>(_ => Task.FromResult(++scans),
            (_, _) => Task.CompletedTask, () => true, clock);
        loop.Start(3000);
        var tick = loop.ScanTickAsync();
        Assert.Equal(2, clock.Count);
        loop.Stop();
        await tick.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, scans);
    }

    [Fact]
    public async Task ConfirmationStopsAsSoonAsDispatchClearsCandidate()
    {
        var clock = new Clock();
        var scans = 0;
        var pending = true;
        using var loop = new ScreenOcrScanLoop<int>(_ => Task.FromResult(++scans),
            (n, _) => { pending = n < 2; return Task.CompletedTask; }, () => pending, clock);
        loop.Start(3000);
        var tick = loop.ScanTickAsync();
        clock.Last.Fire();
        await tick.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, scans);
        Assert.Equal(2, clock.Count);
    }

    [Fact]
    public void FailedScanReportsErrorAndNextTickCanScan()
    {
        var clock = new Clock();
        var scans = 0;
        var errors = new List<Exception>();
        var delivered = 0;
        using var loop = new ScreenOcrScanLoop<int>(_ => ++scans == 1
            ? Task.FromException<int>(new InvalidOperationException("capture")) : Task.FromResult(7),
            (n, _) => { delivered = n; return Task.CompletedTask; }, () => false, clock, errors.Add);
        loop.Start(3000);
        var timer = clock.Last;
        timer.Fire();
        Assert.IsType<InvalidOperationException>(Assert.Single(errors));
        timer.Fire();
        Assert.Equal(7, delivered);
        Assert.Equal(2, scans);
    }
}
