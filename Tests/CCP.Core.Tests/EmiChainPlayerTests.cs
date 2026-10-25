using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Core.Tests;

public sealed class EmiChainPlaybackTests
{
    [Fact]
    public void LandHoldsPoseAndFinishesOnlyAfterLastFrame()
    {
        var clock = new Clock();
        using var player = new EmiChainPlayback(clock, a => a());
        var events = new List<string>();
        player.Play(new EmiChain("land", "PUT DOWN",
            new[] { new EmiFrame("^_^", 600), new EmiFrame("0_0", 160) }, BodyFrame: "celebration"),
            new EmiChainHooks
            {
                BodyFrame = p => events.Add(p), Draw = (f, _, _) => events.Add(f),
                Done = () => events.Add("done")
            });
        Assert.Equal(new[] { "celebration", "^_^" }, events);
        Assert.Equal(600, clock.Delay.TotalMilliseconds);
        clock.Fire();
        Assert.True(player.IsLive);
        Assert.Equal("0_0", events[^1]);
        Assert.Equal(160, clock.Delay.TotalMilliseconds);
        clock.Fire();
        Assert.False(player.IsLive);
        Assert.Equal("done", events[^1]);
    }

    [Fact]
    public void DizzyUsesEveryCanonHoldAndCancelRejectsQueuedTicks()
    {
        var clock = new Clock();
        var posted = new Queue<Action>();
        using var player = new EmiChainPlayback(clock, posted.Enqueue);
        var faces = new List<string>();
        int done = 0;
        var hooks = new EmiChainHooks { Draw = (f, _, _) => faces.Add(f), Done = () => done++ };
        player.Play(EmiChains.Get("dizzy"), hooks);
        foreach (int ms in new[] { 260, 260, 260, 800 })
        {
            Assert.Equal(ms, clock.Delay.TotalMilliseconds);
            clock.Fire(); posted.Dequeue()();
        }
        Assert.Equal(new[] { "@_@", "=_=", "@_@", "x_x" }, faces);
        Assert.Equal(1, done);
        player.Play(EmiChains.Get("dizzy"), hooks);
        clock.Fire();
        player.Cancel();
        player.Play(EmiChains.Get("wink"), hooks);
        posted.Dequeue()(); // an old timer already posted before cancellation
        Assert.Equal("^_^", faces[^1]);
        Assert.True(player.IsLive);
        player.Cancel();
        Assert.False(player.IsLive);
        Assert.Equal(1, done);
    }

    private sealed class Clock : TimeProvider
    {
        private Action? _tick;
        public TimeSpan Delay;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Delay = dueTime;
            _tick = () => callback(state);
            return new Timer();
        }
        public void Fire() => _tick!();
        private sealed class Timer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
