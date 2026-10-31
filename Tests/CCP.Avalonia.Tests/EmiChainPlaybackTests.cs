using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class EmiChainPlaybackTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    // Advance the actual timer's handler, without waiting or changing the canon holds.
    internal static void Tick(EmiChainPlayer player, int hold)
    {
        var timer = (DispatcherTimer)typeof(EmiChainPlayer).GetField("_timer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(player)!;
        Assert.True(timer.IsEnabled);
        Assert.Equal(TimeSpan.FromMilliseconds(hold), timer.Interval);
        typeof(EmiChainPlayer).GetMethod("OnTick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(player, new object[] { timer, EventArgs.Empty });
        Assert.False(timer.IsEnabled);
    }

    [Fact]
    public Task FramesHoldThenCompleteWithOrderedHooks() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        using var player = new EmiChainPlayer(Dispatcher.UIThread);
        var seen = new List<string>();
        var chain = new EmiChain("test", "test", new[]
        {
            new EmiFrame("0_0", 420, true, true, "."),
            new EmiFrame("^_^", 520, SetsBubble: true, Bubble: "hello")
        }, Fx: "hearts", Move: "nod", Flat: true, BodyFrame: "idle");
        player.Play(chain, new EmiChainHooks
        {
            Bubble = s => seen.Add("bubble:" + s), BodyFrame = s => seen.Add("pose:" + s),
            Move = s => seen.Add("move:" + s), Fx = s => seen.Add("fx:" + s),
            Draw = (s, small, flat) => seen.Add($"draw:{s}:{small}:{flat}"), Done = () => seen.Add("done")
        }, "pet");
        Assert.Same(chain, player.Current);
        Assert.Equal(new[] { "bubble:", "pose:pet", "move:nod", "bubble:.", "draw:0_0:True:True", "fx:hearts" }, seen);
        Tick(player, 420);
        Assert.True(player.IsLive);
        Assert.Equal(new[] { "bubble:hello", "draw:^_^:False:True" }, seen.GetRange(6, 2));
        Assert.DoesNotContain("done", seen);
        Tick(player, 520);
        Assert.False(player.IsLive);
        Assert.Null(player.Current);
        Assert.Equal("done", seen[^1]);
        return Task.CompletedTask;
    });

    [Fact]
    public Task ReplacementCancelEmptyAndDisposeRespectDone() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        using var player = new EmiChainPlayer(Dispatcher.UIThread);
        int oldDone = 0, done = 0;
        var chain = new EmiChain("test", "test", new[] { new EmiFrame("0_0", 1) });
        player.Play(chain, new EmiChainHooks { Done = () => oldDone++ });
        var oldTimer = (DispatcherTimer)typeof(EmiChainPlayer).GetField("_timer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(player)!;
        player.Play(chain, new EmiChainHooks { Done = () => done++ });
        Assert.False(oldTimer.IsEnabled);
        Tick(player, 1);
        Assert.Equal(0, oldDone);
        Assert.Equal(1, done);
        player.Play(chain, new EmiChainHooks { Done = () => done++ });
        player.Cancel();
        Assert.False(player.IsLive);
        Assert.Equal(1, done);
        player.Play(chain with { Seq = Array.Empty<EmiFrame>() }, new EmiChainHooks { Done = () => done++ });
        Assert.Equal(2, done);
        player.Play(chain, new EmiChainHooks { Done = () => done++ });
        player.Dispose();
        player.Play(chain, new EmiChainHooks { Done = () => done++ });
        Assert.False(player.IsLive);
        Assert.Equal(2, done);
        return Task.CompletedTask;
    });

    [Fact]
    public Task WorkerPlayMarshalsAndTimerCompletesOnTheDispatcher() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        using var player = new EmiChainPlayer(Dispatcher.UIThread);
        var completion = new TaskCompletionSource();
        bool drewOnUi = false, completedOnUi = false;
        await Task.Run(() => player.Play(new EmiChain("clock", "clock", new[] { new EmiFrame("0_0", 10) }),
            new EmiChainHooks
            {
                Draw = (_, _, _) => drewOnUi = Dispatcher.UIThread.CheckAccess(),
                Done = () => { completedOnUi = Dispatcher.UIThread.CheckAccess(); completion.SetResult(); }
            }));
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(drewOnUi);
        Assert.True(completedOnUi);
        Assert.False(player.IsLive);
    });

    [Fact]
    public Task DeskRoutesCanonChainsAndCancelsOnClose() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var desk = new EmiDeskWindow();
        int done = 0;
        try
        {
            desk.PlayChain("not-a-chain", () => done++);
            Assert.False(desk.ChainLive);
            Assert.Equal(0, done);
            desk.PlayChain("wink", () => done++);
            Assert.True(desk.ChainLive);
            var player = (EmiChainPlayer)typeof(EmiDeskWindow).GetField("_chainPlayer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(desk)!;
            foreach (var frame in EmiChains.Get("wink")!.Seq) Tick(player, Math.Max(1, frame.Ms));
            Assert.Equal(1, done);
            Assert.False(desk.ChainLive);
            Assert.Equal(EmiChains.RestFace, desk.Face);
            desk.PlayChain("wink", () => done++);
            desk.CancelChain();
            Assert.False(desk.ChainLive);
            Assert.Equal(1, done);
            desk.PlayChain("wink", () => done++);
            desk.Close();
            Assert.False(desk.ChainLive);
            Assert.Equal(1, done);
        }
        finally { desk.ShutDown(); }
        return Task.CompletedTask;
    });
}
