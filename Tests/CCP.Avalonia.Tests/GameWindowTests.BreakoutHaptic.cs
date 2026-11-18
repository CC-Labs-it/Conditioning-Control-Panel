using ConditioningControlPanel;
using System.Collections.Generic;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Services.BackRoom;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    [Theory]
    [InlineData("breakout")]
    [InlineData("breakoutdemo")]
    public void Breakout_HapticFramesAndLifecycle(string id) => Run((_, launcher) =>
    {
        launcher.Hide();
        CoreEntitlement.HasLabProvider = () => true;
        var window = GameWindow.Launch(id)!;
        var output = new List<BackRoomHaptic>();
        window.BreakoutHaptic = output.Add;
        void Pulse() => window.HandleMessage("{\"type\":\"haptic\",\"station\":\"breakout\",\"level\":5,\"ms\":99999}");
        Pulse();
        Assert.Equal(1, output[^1].Level);
        Assert.Equal(BackRoomBridge.HapticMaxMs, output[^1].Ms);
        window.HandleMessage("{\"type\":\"haptic\",\"station\":\"breakout\",\"level\":0}");
        Assert.True(output[^1].IsStop);
        Pulse();
        window.HandleMessage("{\"type\":\"station-close\",\"station\":\"breakout\"}");
        Assert.True(output[^1].IsStop);
        Pulse();
        window.RoomMinimizedChanged(true);
        Assert.True(output[^1].IsStop);
        int count = output.Count;
        Pulse();
        Assert.Equal(count, output.Count);
        window.RoomMinimizedChanged(false);
        Pulse();
        Assert.False(output[^1].IsStop);
        GameWindow.HandleBreakoutPanic(System.DateTime.UtcNow);
        Assert.True(output[^1].IsStop);
        count = output.Count;
        Pulse();
        Assert.Equal(count, output.Count);
        window.ResumeBreakoutAfterPanic(System.DateTime.UtcNow.AddSeconds(3));
        Pulse();
        window.HandleMessage("{\"type\":\"exit\"}");
        Assert.True(output[^1].IsStop);
        count = output.Count;
        Pulse();
        Assert.Equal(count, output.Count);
        count = output.Count;
        window.Close();
        Assert.Equal(count + 1, output.Count);
        Assert.True(output[^1].IsStop);
        count = output.Count;
        Pulse();
        Assert.Equal(count, output.Count);
    });
}
