using System;
using Avalonia;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Services.Possession;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class PossessionShellTests
{
    private sealed class NoopDisposable : IDisposable { public void Dispose() { } }

    /// <summary>WPF MainWindow.Possession.cs:687: an open For You feed (FypHostService.IsActive, ghosted too)
    /// stands the haunt down; closing it lets the room be haunted again.</summary>
    [Fact]
    public void OpenForYouFeed_GhostedOrNot_StandsPossessionDown() => Run(rig =>
    {
        var old = (GameWindow.FypGhostSupport, GameWindow.FypGhostMirror, GameWindow.FypGhostMirrorOpacity,
            GameWindow.FypGhostButtonConfigure, GameWindow.FypGhostStartTick);
        GameWindow.FypGhostSupport = () => null;
        GameWindow.FypGhostMirror = (_, _, _) => (new NoopDisposable(), null);
        GameWindow.FypGhostMirrorOpacity = (_, _) => { };
        GameWindow.FypGhostButtonConfigure = (_, _, passive) => passive;
        GameWindow.FypGhostStartTick = _ => new NoopDisposable();
        var host = (IPossessionHost)rig.Shell;
        var feed = new GameWindow(GameWindow.Games[GameWindow.FypId]);
        try
        {
            Assert.True(host.IsUsable);
            feed.Show();
            Frame();
            Assert.False(host.IsUsable);

            feed.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"clickThrough\",\"value\":true}");
            Assert.True(feed.IsFypGhosted);
            Assert.False(GameWindow.IsForYouOnScreen());   // the mandatory video / bubble count holds let go here
            Assert.False(host.IsUsable);                    // possession does not: WPF gates on IsActive

            feed.Close();
            Frame();
            Assert.True(host.IsUsable);
        }
        finally
        {
            feed.Close();
            (GameWindow.FypGhostSupport, GameWindow.FypGhostMirror, GameWindow.FypGhostMirrorOpacity,
                GameWindow.FypGhostButtonConfigure, GameWindow.FypGhostStartTick) = old;
        }
    });
}
