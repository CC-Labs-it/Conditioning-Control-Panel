using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>fix3 (openbox + picom): the attached tube did not follow the shell while it was dragged. A
/// reparenting X11 WM reports the move to the client only when it ends, so the tube follows main's real
/// position read from the server while main may be moving (WPF follows LocationChanged live).</summary>
public sealed class TubeFollowDragTests
{
    [Fact]
    public Task APressOnMainFollowsItsLivePositionUntilTheMoveSettles() => Run((main, tube) =>
    {
        var live = new PixelPoint(2000, 200);    // the server's view of main (client origin)
        bool button = true;
        tube.ReadRootOrigin = w => w == main ? live : null;
        tube.ReadButtonDown = () => button;
        var before = tube.Position;

        main.MouseDown(new Point(20, 10), MouseButton.Left);   // e.g. the title bar: BeginMoveDrag follows
        Assert.True(tube.FollowPollRunning);

        live = new PixelPoint(2150, 280);        // the WM moved main; Avalonia's main.Position is still stale
        tube.FollowTick();
        var followed = tube.Position;
        Assert.NotEqual(before, followed);

        // Exactly where the ordinary dock puts her once main's move is reported (this also reports it).
        main.Position = new PixelPoint(2150, 280);
        tube.UpdatePosition();
        Assert.Equal(tube.Position, followed);

        for (int i = 0; i < AvatarTubeWindow.FollowStillTicks + 2; i++) tube.FollowTick();
        Assert.True(tube.FollowPollRunning);     // button still held: the move may go on

        button = false;
        for (int i = 0; i < AvatarTubeWindow.FollowStillTicks; i++) tube.FollowTick();
        Assert.False(tube.FollowPollRunning);    // released, still and reported by the WM: done
        main.MouseUp(new Point(20, 10), MouseButton.Left);
    });

    [Fact]
    public Task AMoveThatOutlivesTheButtonIsStillFollowed() => Run((main, tube) =>
    {
        // A quick press-drag-release can end before the WM's grab starts; the WM's move then runs on.
        var live = new PixelPoint(2000, 200);
        var now = new DateTime(2026, 11, 6, 22, 0, 0, DateTimeKind.Utc);
        tube.ReadRootOrigin = _ => live;
        tube.ReadButtonDown = () => false;
        tube.FollowClock = () => now;
        tube.StartFollowPoll();
        // Button already up and nothing moved yet: not over, the WM has not reported - look slowly.
        for (int i = 0; i < AvatarTubeWindow.FollowStillTicks; i++) tube.FollowTick();
        Assert.True(tube.FollowPollIdle);
        now = now.AddSeconds(5);                  // the user moves the mouse later: main follows it
        for (int i = 0; i < 5; i++)
        {
            live = new PixelPoint(live.X - 40, live.Y + 10);
            tube.FollowTick();
            Assert.True(tube.FollowPollRunning);  // still moving: keep following, at full rate
            Assert.False(tube.FollowPollIdle);
        }
        var expected = tube.Position;
        // The quiet limit ends a poll whose move never reports and never moves again.
        for (int i = 0; i < AvatarTubeWindow.FollowStillTicks; i++) tube.FollowTick();
        Assert.True(tube.FollowPollIdle);
        now = now.AddSeconds(16);
        tube.FollowTick();
        Assert.False(tube.FollowPollRunning);

        main.Position = live;                     // the WM's final report: same dock
        tube.UpdatePosition();
        Assert.Equal(expected, tube.Position);
    });

    [Fact]
    public Task NothingRunsOffX11OrWhileDetached() => Run((main, tube) =>
    {
        tube.ReadRootOrigin = _ => null;         // Windows, headless, native Wayland
        main.MouseDown(new Point(20, 10), MouseButton.Left);
        main.MouseUp(new Point(20, 10), MouseButton.Left);
        Assert.False(tube.FollowPollRunning);

        tube.ReadRootOrigin = _ => new PixelPoint(2000, 200);
        tube.Detach();
        tube.StartFollowPoll();
        Assert.False(tube.FollowPollRunning);    // detached she stays where she was left
        tube.Attach();
    });

    private static Task Run(Action<Window, AvatarTubeWindow> body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().WithInterFont()
                .With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreSettings.Current.AvatarTubeDetached = false;
        CoreSettings.Current.AvatarTubeScale = 1.0;
        var main = new Window { Width = 1000, Height = 700, Position = new PixelPoint(2000, 200) };
        AvatarTubeWindow? tube = null;
        try
        {
            main.Show();
            tube = new AvatarTubeWindow(main);
            tube.Show();
            Dispatcher.UIThread.RunJobs();
            body(main, tube);
        }
        finally
        {
            tube?.Close();
            main.Close();
            Dispatcher.UIThread.RunJobs();
            service.SaveImmediate();
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });
}
