using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Services.Haptics;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    [Fact]
    public void Dtrh_HapticsStopOnCloseDuringRun() => RunDtrh(lab: true, (_, _, _, _) =>
    {
        var w = GameWindow.Launch("dtrh")!;
        Assert.True(DtrhHapticDirector.Snapshot.Active);
        w.HandleMessage("{\"type\":\"run-started\"}");
        Assert.True(DtrhHapticDirector.Snapshot.RunActive);
        w.HandleMessage("{\"type\":\"run-ended\"}");
        Assert.False(DtrhHapticDirector.Snapshot.RunActive);
        w.HandleMessage("{\"type\":\"run-started\"}");
        Assert.True(DtrhHapticDirector.Snapshot.RunActive);
        w.Close();
        Assert.False(DtrhHapticDirector.Snapshot.Active);
        Assert.False(DtrhHapticDirector.Snapshot.RunActive);
    });
}
