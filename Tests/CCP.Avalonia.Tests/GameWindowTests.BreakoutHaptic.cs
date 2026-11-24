using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services.BackRoom;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    [Theory]
    [InlineData("breakoutdemo")]
    [InlineData("breakout")]
    public void BreakoutHapticFramesAndPanic(string id) => Run((_, _) =>
    {
        ConditioningControlPanel.CoreEntitlement.HasLabProvider = () => true;
        var w = ConditioningControlPanel.Avalonia.Views.Games.GameWindow.Launch(id)!;
        var pulses = new List<BackRoomHaptic>();
        w.BreakoutHaptic = pulses.Add;
        w.Web.OnNavigationCompleted(w.PageUrl!);
        const string pulse = "{\"type\":\"haptic\",\"station\":\"breakout\",\"level\":0.6,\"ms\":500}";
        w.Web.OnWebMessage(pulse);
        Assert.Single(pulses);
        Assert.Equal(0.6, pulses[0].Level);
        w.Web.OnWebMessage("{\"type\":\"haptic\",\"station\":\"other\",\"level\":1,\"ms\":500}");
        Assert.Single(pulses);
        w.Web.OnWebMessage("{\"type\":\"station-close\",\"station\":\"breakout\"}");
        Assert.True(pulses[^1].IsStop);
        w.RoomMinimizedChanged(true);
        Assert.True(pulses[^1].IsStop);
        int quietCount = pulses.Count;
        w.Web.OnWebMessage(pulse);
        Assert.Equal(quietCount, pulses.Count);
        w.RoomMinimizedChanged(false);
        Assert.True(ConditioningControlPanel.Avalonia.Views.Games.GameWindow.HandleBreakoutPanic(DateTime.Now));
        Assert.True(pulses[^1].IsStop);
        int count = pulses.Count;
        w.Web.OnWebMessage(pulse);
        Assert.Equal(count, pulses.Count);
        w.Close();
    });
}
