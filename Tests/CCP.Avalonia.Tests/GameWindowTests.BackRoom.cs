using System.Collections.Generic;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    // WPF MainWindow.Exclusives.cs:204: the Back Room card opens the Play wall's door (BtnStartBackRoom_Click).
    // The host registration is PR #3553; this head's card follows it exactly as the Play door does.
    [Fact]
    public void BackRoomCardFollowsTheHostAndOpensThePlayDoor() => Run((shell, _) =>
    {
        var old = PlayTabView.LaunchGame;
        var doors = new List<string>();
        PlayTabView.LaunchGame = doors.Add;
        GameWindow.Games.TryGetValue("backroom", out var host);
        GameWindow.Games["backroom"] = host ?? new("backroom", "launcher_game_backroom_title", "");
        try
        {
            Assert.True(ExclusivesTabView.IsOnThisBuild("backroom"));
            shell.OpenExclusiveFeature("backroom");
            Assert.Equal("backroom", Assert.Single(doors));
            GameWindow.Games.Remove("backroom");
            Assert.False(ExclusivesTabView.IsOnThisBuild("backroom"));
        }
        finally
        {
            PlayTabView.LaunchGame = old;
            if (host != null) GameWindow.Games["backroom"] = host;
            else GameWindow.Games.Remove("backroom");
        }
    });
}
