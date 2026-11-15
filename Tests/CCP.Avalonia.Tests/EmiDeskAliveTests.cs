using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

public class EmiDeskAliveTests
{
    [Fact]
    public Task GazeFollowsPointerYieldsToChainAndResetsOnHide() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var desk = new EmiDeskWindow { AliveWaylandOverride = false };
        try
        {
            desk.Show(); Dispatcher.UIThread.RunJobs();
            var body = desk.BodyScreenRect;
            desk.AlivePointer = () => new PixelPoint((int)(body.X - 200), (int)(body.Y + body.Height / 2));
            desk.PollGaze();
            double lean = desk.AliveGazeX;
            Assert.True(lean < 0);
            Assert.True(Math.Abs(lean) <= EmiAlive.GazeMaxDip * EmiAlive.GazeScale(desk.BodyWidth));
            desk.AlivePointer = () => null;
            desk.PollGaze();
            Assert.Equal(EmiAlive.Ease(lean, 0, EmiAlive.GazeEasePerPoll), desk.AliveGazeX, 8);
            desk.AlivePointer = () => new PixelPoint((int)(body.X - 200), (int)body.Y);
            desk.PlayChain("glance");
            desk.PollGaze();
            Assert.True(Math.Abs(desk.AliveGazeX) < Math.Abs(lean));
            desk.Hide(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, desk.AliveGazeX);
        }
        finally { desk.ShutDown(); }
        return Task.CompletedTask;
    });
    [Fact]
    public Task WaylandSurfacePointerOverridesStaleGlobalAndExpiresOnExit() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var desk = new EmiDeskWindow { AliveWaylandOverride = true, AliveX11Override = true };
        try
        {
            desk.Show(); Dispatcher.UIThread.RunJobs();
            var body = desk.FindControl<Grid>("BodyRoot")!;
            var stale = desk.BodyScreenRect;
            desk.AliveXPointer = () => (new PixelPoint((int)(stale.Right + 300), (int)stale.Y), false);
            var left = body.TranslatePoint(new Point(body.Bounds.Width * .2, body.Bounds.Height * .4), desk)!.Value;
            desk.MouseMove(left); Dispatcher.UIThread.RunJobs();
            desk.PollGaze();
            Assert.True(desk.AliveGazeX < 0);
            desk.MouseMove(new Point(-100, -100)); Dispatcher.UIThread.RunJobs();
            var before = desk.AliveGazeX;
            desk.PollGaze();
            // With no chain, accepting the stale X position would lean the other way.
            Assert.Equal(EmiAlive.Ease(before, 0, EmiAlive.GazeEasePerPoll), desk.AliveGazeX, 8);
            before = desk.AliveGazeX;
            desk.PlayChain("glance");
            Assert.True(desk.ChainLive);
            desk.PollGaze();
            Assert.Equal(EmiAlive.Ease(before, 0, EmiAlive.GazeEasePerPoll), desk.AliveGazeX, 8);
            desk.Hide(); desk.Show(); Dispatcher.UIThread.RunJobs();
            desk.PollGaze();
            Assert.Equal(0, desk.AliveGazeX); // a previous surface position cannot survive hide
        }
        finally { desk.ShutDown(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task WaylandAcceptsConfirmedXPointerOnlyForX11Desk() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var desk = new EmiDeskWindow { AliveWaylandOverride = true, AliveX11Override = true };
        try
        {
            desk.Show(); Dispatcher.UIThread.RunJobs();
            var body = desk.BodyScreenRect;
            desk.AliveXPointer = () => (new PixelPoint((int)(body.X - 200), (int)body.Y), true);
            desk.PollGaze();
            Assert.True(desk.AliveGazeX < 0);
            desk.Hide(); desk.Show(); Dispatcher.UIThread.RunJobs();
            desk.AliveX11Override = false;
            desk.PollGaze();
            Assert.Equal(0, desk.AliveGazeX); // native Wayland has no comparable screen origin
            desk.AliveX11Override = true;
            desk.AliveXPointer = () => null;
            desk.PollGaze();
            Assert.Equal(0, desk.AliveGazeX);
        }
        finally { desk.ShutDown(); }
        return Task.CompletedTask;
    });
}
