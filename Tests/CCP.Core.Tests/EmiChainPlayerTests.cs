using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Core.Tests;

public sealed class EmiChainPlayerTests
{
    [Fact]
    public void FramesKeepTheirHoldsAndHooksRunInWpfOrder()
    {
        var clock = new Clock();
        using var player = new EmiChainPlayer(() => true, a => a(), clock.Start);
        var calls = new List<string>();
        bool finished = false;
        var chain = new EmiChain("test", "test", new[]
        {
            new EmiFrame("first", 40, Small: true, SetsBubble: true, Bubble: "hello"),
            new EmiFrame("last", 80, SetsBubble: true)
        }, Fx: "hearts", Move: "bounce", Flat: true, BodyFrame: "idle");
        player.Play(chain, new EmiChainHooks
        {
            Bubble = text => calls.Add("bubble:" + text),
            BodyFrame = pose => calls.Add("pose:" + pose),
            Move = move => calls.Add("move:" + move),
            Draw = (text, small, flat) => calls.Add($"draw:{text}:{small}:{flat}"),
            Fx = fx => calls.Add("fx:" + fx),
            Done = () => { calls.Add("done"); finished = true; }
        }, "pet");
        Assert.True(player.IsLive);
        Assert.Same(chain, player.Current);
        Assert.Equal(new[] { "bubble:", "pose:pet", "move:bounce", "bubble:hello",
            "draw:first:True:True", "fx:hearts" }, calls);
        Assert.Equal(TimeSpan.FromMilliseconds(40), clock.Hold);
        clock.Elapse();
        Assert.Equal(TimeSpan.FromMilliseconds(80), clock.Hold); // the LAST frame holds too
        Assert.False(finished);
        clock.Elapse();
        Assert.True(finished);
        Assert.False(player.IsLive);
        Assert.Null(player.Current);
        Assert.Null(clock.Hold);
        Assert.Equal(new[] { "bubble:", "draw:last:False:True", "done" }, calls.GetRange(6, 3));
    }

    [Fact]
    public void ReplacementCancellationAndDisposalNeverCompleteTheOldChain()
    {
        var clock = new Clock();
        var player = new EmiChainPlayer(() => true, a => a(), clock.Start);
        int done = 0;
        var frames = new List<string>();
        var hooks = new EmiChainHooks { Draw = (text, _, _) => frames.Add(text), Done = () => done++ };
        var chain = new EmiChain("test", "test", new[] { new EmiFrame("one", 30), new EmiFrame("two", 30) });
        player.Play(chain, hooks);
        player.Play(chain, hooks);
        player.Cancel();
        Assert.Null(clock.Hold); // no timer left to draw "two" or complete
        Assert.Equal(new[] { "one", "one" }, frames);
        Assert.Equal(0, done);
        Assert.False(player.IsLive);
        player.Play(null, hooks); // WPF completes an empty sequence immediately
        Assert.Equal(1, done);
        player.Play(chain, hooks);
        player.Dispose();
        Assert.Null(clock.Hold);
        player.Play(chain, hooks);
        Assert.Null(clock.Hold);
        Assert.Equal(3, frames.Count);
        Assert.Equal(1, done);
        Assert.False(player.IsLive);
    }
    [Fact]
    public void OffThreadPlayPostsBeforeStartingTheClock()
    {
        var clock = new Clock(); bool access = false; Action? posted = null;
        using var player = new EmiChainPlayer(() => access, a => posted = a, clock.Start);
        player.Play(EmiChains.Get("wake"), new EmiChainHooks());
        Assert.False(player.IsLive); Assert.Null(clock.Hold); Assert.NotNull(posted);
        access = true; posted();
        Assert.True(player.IsLive); Assert.Equal(TimeSpan.FromMilliseconds(500), clock.Hold);
    }

    private sealed class Clock
    {
        public TimeSpan? Hold;
        private Action? _tick;
        public Action Start(TimeSpan hold, Action tick)
        {
            Hold = hold; _tick = tick;
            return () => { Hold = null; _tick = null; };
        }
        public void Elapse() => _tick?.Invoke();
    }
}
