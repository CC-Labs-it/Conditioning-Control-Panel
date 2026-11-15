using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
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
        var desk = new EmiDeskWindow();
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
            Assert.Equal(lean, desk.AliveGazeX);
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
}
