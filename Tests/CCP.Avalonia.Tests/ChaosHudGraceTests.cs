using Avalonia;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosHudGraceTests
{
    [Fact]
    public void Live_pointer_replaces_stale_exit_position()
    {
        var panel = new Rect(0, 0, 100, 100);
        var last = new Point(105, 50);
        Assert.True(ChaosHudWindow.CursorInHudGrace(panel, default, new Point(110, 50), last, false));
        Assert.False(ChaosHudWindow.CursorInHudGrace(panel, default, new Point(150, 50), last, false));
    }

    [Fact]
    public void Unavailable_pointer_does_not_poll_a_stale_exit_forever()
    {
        var panel = new Rect(0, 0, 100, 100);
        var last = new Point(105, 50);
        Assert.True(ChaosHudWindow.CursorInHudGrace(panel, default, null, last, true));
        Assert.False(ChaosHudWindow.CursorInHudGrace(panel, default, null, last, false));
    }
}
