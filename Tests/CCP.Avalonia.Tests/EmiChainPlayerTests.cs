using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class EmiChainPlayerTests
{
    [Fact]
    public Task FramesKeepTheirHoldsAndHooksRunInWpfOrder() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        using var player = new EmiChainPlayer(Dispatcher.UIThread);
        var calls = new List<string>();
        var finished = new TaskCompletionSource();
        var chain = new EmiChain("test", "test", new[]
        {
            new EmiFrame("first", 40, Small: true, SetsBubble: true, Bubble: "hello"),
            new EmiFrame("last", 80, SetsBubble: true)
        }, Fx: "hearts", Move: "bounce", Flat: true, BodyFrame: "idle");
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        player.Play(chain, new EmiChainHooks
        {
            Bubble = text => calls.Add("bubble:" + text),
            BodyFrame = pose => calls.Add("pose:" + pose),
            Move = move => calls.Add("move:" + move),
            Draw = (text, small, flat) => calls.Add($"draw:{text}:{small}:{flat}"),
            Fx = fx => calls.Add("fx:" + fx),
            Done = () => { calls.Add("done"); finished.SetResult(); }
        }, "pet");
        Assert.True(player.IsLive);
        Assert.Same(chain, player.Current);
        Assert.Equal(new[] { "bubble:", "pose:pet", "move:bounce", "bubble:hello",
            "draw:first:True:True", "fx:hearts" }, calls);
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(elapsed.ElapsedMilliseconds >= 100); // includes the LAST frame hold
        Assert.False(player.IsLive);
        Assert.Null(player.Current);
        Assert.Equal(new[] { "bubble:", "draw:last:False:True", "done" }, calls.GetRange(6, 3));
    });

    [Fact]
    public Task ReplacementCancellationAndDisposalNeverCompleteTheOldChain() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var player = new EmiChainPlayer(Dispatcher.UIThread);
        int done = 0;
        var frames = new List<string>();
        var hooks = new EmiChainHooks { Draw = (text, _, _) => frames.Add(text), Done = () => done++ };
        var chain = new EmiChain("test", "test", new[] { new EmiFrame("one", 30), new EmiFrame("two", 30) });
        player.Play(chain, hooks);
        player.Play(chain, hooks);
        player.Cancel();
        await Task.Delay(120);
        Assert.Equal(new[] { "one", "one" }, frames);
        Assert.Equal(0, done);
        Assert.False(player.IsLive);
        player.Play(null, hooks); // WPF completes an empty sequence immediately
        Assert.Equal(1, done);
        player.Play(chain, hooks);
        player.Dispose();
        player.Play(chain, hooks);
        await Task.Delay(120);
        Assert.Equal(3, frames.Count);
        Assert.Equal(1, done);
        Assert.False(player.IsLive);
    });
}

public sealed class EmiDeskChainWiringTests
{
    [Fact]
    public Task DeskUsesTheFramePlayerAndShutdownCancelsIt() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont()
                .With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var desk = EmiDeskService.Instance.BeginPresentation()!;
        try
        {
            int done = 0;
            desk.PlayChain("not-a-chain", () => done++);
            Assert.False(desk.ChainLive);
            Assert.Equal(0, done); // unknown ids do not complete a caller's continuation
            desk.PlayChain("wake", () => done++);
            Assert.True(desk.ChainLive);
            global::ConditioningControlPanel.Avalonia.Views.Windows.PanicSurfaces.All
                .Single(surface => surface.Id == "emi-chain").Stop(null);
            Assert.False(desk.ChainLive);
            Assert.Equal(0, done);
            var complete = new TaskCompletionSource();
            desk.PlayChain(new EmiChain("test", "test", new[] { new EmiFrame("^_~", 30) }),
                () => complete.SetResult());
            Assert.Equal("^_~", desk.Face);
            await complete.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.False(desk.ChainLive);
            Assert.Equal(EmiChains.RestFace, desk.Face);
            desk.Say("hello");
            Assert.True(desk.ChainLive);
            desk.CancelChain();
            desk.RunSummon();
            desk.StopChainForPanic(); // before smoke/CRT callbacks can create a wake chain
            await Task.Delay(800);
            Assert.False(desk.ChainLive);
            Assert.False(desk.InputLocked);
            Assert.Equal(EmiChains.RestFace, desk.Face);
            desk.Say("hello again");
            Assert.True(desk.ChainLive);
            desk.ShutDown();
            Assert.False(desk.ChainLive);
        }
        finally { desk.ShutDown(); }
    });
}
