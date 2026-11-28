using System;
using System.Linq;
using ConditioningControlPanel.Avalonia.Views.Games;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    [Fact]
    public void GoonWatch_PingsOnlyAfterReadyAndPanicCancelsRecovery() => Run((shell, launcher) =>
    {
        var old = GameWindow.GoonUtcNow;
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        GameWindow.GoonUtcNow = () => now;
        try
        {
            var w = new GameWindow(new GameWindow.Game("goon", "launcher_game_goon_title", "goon/index.html"));
            w.Load(GameWindow.Server()); w.Show();
            var pings = 0;
            w.Posted += json => { if (json.Contains("\"ping\"")) pings++; };
            now = now.AddSeconds(30); w.CheckGoonWatch(now);
            Assert.Contains(w, GameWindow.OpenWindowsForTests());
            Assert.Equal(0, pings);
            w.HandleMessage("{\"type\":\"ready\"}");
            now = now.AddSeconds(9); w.CheckGoonWatch(now);
            Assert.Equal(1, pings);
            w.Closed += (_, _) => GameWindow.CloseAllForPanic();
            now = now.AddSeconds(12); w.CheckGoonWatch(now);
            Assert.DoesNotContain(GameWindow.OpenWindowsForTests(), x => x.Spec.Id == "goon");
        }
        finally { GameWindow.GoonUtcNow = old; }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GoonWatch_RecoversOnceFromSilentOrFrozenPage(bool frozen) => Run((shell, launcher) =>
    {
        var old = GameWindow.GoonUtcNow;
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        GameWindow.GoonUtcNow = () => now;
        try
        {
            var w = new GameWindow(new GameWindow.Game("goon", "launcher_game_goon_title", "goon/index.html"));
            w.Load(GameWindow.Server());
            w.Show();
            w.HandleMessage("{\"type\":\"ready\"}");
            if (frozen) w.HandleMessage("{\"type\":\"heartbeat\",\"paint\":1,\"vis\":\"visible\"}");
            for (var i = 0; i < 5; i++)
            {
                now = now.AddSeconds(5);
                if (frozen) w.HandleMessage("{\"type\":\"pong\",\"paint\":1,\"vis\":\"visible\"}");
                w.CheckGoonWatch(now);
            }
            var next = Assert.Single(GameWindow.OpenWindowsForTests().Where(x => x.Spec.Id == "goon"));
            Assert.NotSame(w, next);
            next.HandleMessage("{\"type\":\"ready\"}");
            for (var i = 0; i < 5; i++) { now = now.AddSeconds(5); next.CheckGoonWatch(now); }
            Assert.DoesNotContain(next, GameWindow.OpenWindowsForTests());
            Assert.DoesNotContain(GameWindow.OpenWindowsForTests(), x => x.Spec.Id == "goon");
        }
        finally { GameWindow.GoonUtcNow = old; }
    });

    [Fact]
    public void GoonWatch_HiddenPageAndLateUiTickDoNotRecover() => Run((shell, launcher) =>
    {
        var old = GameWindow.GoonUtcNow;
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        GameWindow.GoonUtcNow = () => now;
        try
        {
            var w = new GameWindow(new GameWindow.Game("goon", "launcher_game_goon_title", "goon/index.html"));
            w.Load(GameWindow.Server()); w.Show(); w.HandleMessage("{\"type\":\"ready\"}");
            w.HandleMessage("{\"type\":\"heartbeat\",\"paint\":1}");
            for (var i = 0; i < 6; i++)
            {
                now = now.AddSeconds(5);
                w.HandleMessage("{\"type\":\"heartbeat\",\"paint\":1,\"vis\":\"hidden\"}");
                w.CheckGoonWatch(now);
            }
            now = now.AddSeconds(30);
            w.HandleMessage("{\"type\":\"pong\",\"paint\":1,\"vis\":\"visible\"}");
            w.CheckGoonWatch(now);
            Assert.Contains(w, GameWindow.OpenWindowsForTests());
            GameWindow.CloseAllForPanic();
            Assert.DoesNotContain(w, GameWindow.OpenWindowsForTests());
        }
        finally { GameWindow.GoonUtcNow = old; }
    });
}
