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
    public Task FramesKeepTheirHoldsAndHooksRunInWpfOrder() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        using var player = new EmiChainPlayer(Dispatcher.UIThread);
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
        Assert.Equal(TimeSpan.FromMilliseconds(40), player.PendingHold);
        player.ElapsePendingHold();
        Assert.Equal(TimeSpan.FromMilliseconds(80), player.PendingHold); // the LAST frame holds too
        Assert.False(finished);
        player.ElapsePendingHold();
        Assert.True(finished);
        Assert.False(player.IsLive);
        Assert.Null(player.Current);
        Assert.Null(player.PendingHold);
        Assert.Equal(new[] { "bubble:", "draw:last:False:True", "done" }, calls.GetRange(6, 3));
        return Task.CompletedTask;
    });

    [Fact]
    public Task ReplacementCancellationAndDisposalNeverCompleteTheOldChain() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var player = new EmiChainPlayer(Dispatcher.UIThread);
        int done = 0;
        var frames = new List<string>();
        var hooks = new EmiChainHooks { Draw = (text, _, _) => frames.Add(text), Done = () => done++ };
        var chain = new EmiChain("test", "test", new[] { new EmiFrame("one", 30), new EmiFrame("two", 30) });
        player.Play(chain, hooks);
        player.Play(chain, hooks);
        player.Cancel();
        Assert.Null(player.PendingHold); // no timer left to draw "two" or complete
        Assert.Equal(new[] { "one", "one" }, frames);
        Assert.Equal(0, done);
        Assert.False(player.IsLive);
        player.Play(null, hooks); // WPF completes an empty sequence immediately
        Assert.Equal(1, done);
        player.Play(chain, hooks);
        player.Dispose();
        Assert.Null(player.PendingHold);
        player.Play(chain, hooks);
        Assert.Null(player.PendingHold);
        Assert.Equal(3, frames.Count);
        Assert.Equal(1, done);
        Assert.False(player.IsLive);
        return Task.CompletedTask;
    });
}

public sealed class EmiDeskChainWiringTests
{
    private static EmiDeskWindow OpenDesk(List<Action> deferred)
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont()
                .With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var desk = EmiDeskService.Instance.BeginPresentation()!;
        desk.AfterScheduler = (_, step) => deferred.Add(step); // P08: FX delays run by hand
        return desk;
    }

    private static void RunDeferred(List<Action> deferred)
    {
        for (int guard = 0; deferred.Count > 0 && guard < 100; guard++)
        {
            var step = deferred[0];
            deferred.RemoveAt(0);
            step();
        }
    }

    private static void Panic() =>
        global::ConditioningControlPanel.Avalonia.Views.Windows.PanicSurfaces.All
            .Single(surface => surface.Id == "emi-chain").Stop(null);

    [Fact]
    public Task DeskUsesTheFramePlayerAndShutdownCancelsIt() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var deferred = new List<Action>();
        var desk = OpenDesk(deferred);
        try
        {
            int done = 0;
            desk.PlayChain("not-a-chain", () => done++);
            Assert.False(desk.ChainLive);
            Assert.Equal(0, done); // unknown ids do not complete a caller's continuation
            desk.PlayChain("wake", () => done++);
            Assert.True(desk.ChainLive);
            Panic();
            Assert.False(desk.ChainLive);
            Assert.Equal(0, done);
            bool complete = false;
            desk.PlayChain(new EmiChain("test", "test", new[] { new EmiFrame("^_~", 30) }),
                () => complete = true);
            Assert.Equal("^_~", desk.Face);
            desk.ChainPlayer.ElapsePendingHold();
            Assert.True(complete);
            Assert.False(desk.ChainLive);
            Assert.Equal(EmiChains.RestFace, desk.Face);
            desk.Say("hello");
            Assert.True(desk.ChainLive);
            desk.CancelChain();
            desk.RunSummon();
            Assert.NotEmpty(deferred);
            desk.StopChainForPanic(); // before smoke/CRT callbacks can create a wake chain
            RunDeferred(deferred);
            Assert.False(desk.ChainLive);
            Assert.False(desk.InputLocked);
            Assert.Equal(EmiChains.RestFace, desk.Face);
            desk.Say("hello again");
            Assert.True(desk.ChainLive);
            desk.ShutDown();
            Assert.False(desk.ChainLive);
        }
        finally { desk.AfterScheduler = null; desk.ShutDown(); }
        return Task.CompletedTask;
    });

    /// <summary>WPF EmiDeskWindow.React.cs:134-139: a pat never cuts a line, but cuts her entrance
    /// and still runs the summon's continuation.</summary>
    [Fact]
    public Task APatCutsTheEntranceButNotALine() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var deferred = new List<Action>();
        var desk = OpenDesk(deferred);
        try
        {
            desk.Say("hello");
            var line = desk.ChainPlayer.Current;
            desk.PetFromClick();
            Assert.Same(line, desk.ChainPlayer.Current);
            desk.CancelChain();

            bool arrived = false;
            desk.RunSummon(() => arrived = true);
            RunDeferred(deferred);
            Assert.Equal("wake", desk.ChainPlayer.Current?.Id);
            Assert.False(arrived);
            desk.PetFromClick();
            Assert.True(arrived);
            Assert.NotEqual("wake", desk.ChainPlayer.Current?.Id);
        }
        finally { desk.AfterScheduler = null; desk.ShutDown(); }
        return Task.CompletedTask;
    });

    /// <summary>P06: panic during the goodbye wink still lands the dismiss, exactly once.</summary>
    [Fact]
    public Task PanicDuringTheDismissWinkStillFinishesTheDismiss() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var deferred = new List<Action>();
        var desk = OpenDesk(deferred);
        try
        {
            desk.Show();
            int gone = 0;
            desk.RunDismiss(() => gone++);
            Assert.Equal("wink", desk.ChainPlayer.Current?.Id);
            Panic();
            Assert.Equal(1, gone);
            Assert.False(desk.ChainLive);
            Assert.False(desk.IsVisible);
            Assert.False(desk.InputLocked);
            Assert.False(desk.Transiting);
            RunDeferred(deferred);
            Panic();
            Assert.Equal(1, gone);
        }
        finally { desk.AfterScheduler = null; desk.ShutDown(); }
        return Task.CompletedTask;
    });
}
