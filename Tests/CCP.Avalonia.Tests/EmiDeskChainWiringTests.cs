using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class EmiDeskChainWiringTests
{
    private static EmiDeskWindow OpenDesk(List<Action> deferred)
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
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
            .Single(surface => surface.Id == "emi-desk-feedback").Stop(null);
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
            desk.RunSummon();
            desk.RunDismiss();
            RunDeferred(deferred);
            Assert.Equal("wink", desk.ChainPlayer.Current?.Id); // stale intro cannot replace goodbye
            desk.StopChainForPanic();
            desk.BeginPresentation(() => { });
            desk.Say("not during the show");
            Assert.False(desk.ChainLive);
            desk.EndPresentation();
            desk.Say("hello again");
            Assert.True(desk.ChainLive);
            desk.ShutDown();
            Assert.False(desk.ChainLive);
            desk.PlayChain("wake");
            Assert.False(desk.ChainLive);
            var closed = new EmiDeskWindow();
            closed.PlayChain("wake");
            closed.Close();
            Assert.False(closed.ChainLive);
            closed.PlayChain("wake");
            Assert.False(closed.ChainLive);
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
    [Theory, InlineData("say"), InlineData("dizzy")]
    public Task RightClickPreservesChainButCutsWake(string id) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var deferred = new List<Action>(); var desk = OpenDesk(deferred);
        try {
            desk.Show(); Dispatcher.UIThread.RunJobs();
            var body = desk.FindControl<Control>("BodyRoot")!;
            void Click() {
                var point = body.TranslatePoint(new Point(body.Bounds.Width / 2, body.Bounds.Height / 2), desk)!.Value;
                desk.MouseDown(point, MouseButton.Right); desk.MouseUp(point, MouseButton.Right);
            }
            if (id == "say") desk.Say("hello"); else desk.PlayChain(id);
            var chain = desk.ChainPlayer.Current; Assert.NotNull(chain);
            Click(); Assert.Same(chain, desk.ChainPlayer.Current); Assert.True(desk.RingOpen); desk.CloseRing();
            bool arrived = false; desk.RunSummon(() => arrived = true); RunDeferred(deferred);
            Assert.Equal("wake", desk.ChainPlayer.Current?.Id); Assert.False(arrived);
            Click(); Assert.True(arrived); Assert.False(desk.ChainLive); Assert.True(desk.RingOpen);
        }
        finally { desk.ShutDown(); }
        return Task.CompletedTask; });
}
