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
    [Fact]
    public void Xwayland_stale_halo_expires_after_leaving_for_native_wayland_window()
    {
        var panel = new Rect(0, 0, 100, 100);
        var stale = new Point(105, 50);
        var limit = ChaosHudWindow.WAYLAND_HALO_LIMIT_MS;
        // XQueryPointer keeps returning the last X window position after the leave.
        Assert.True(ChaosHudWindow.CursorInHudGrace(panel, default, stale, stale, false, true, limit - 1));
        Assert.False(ChaosHudWindow.CursorInHudGrace(panel, default, stale, stale, false, true, limit));
        // A confirmed hover stays open, and a real X11 query is not subject to this fallback.
        Assert.True(ChaosHudWindow.CursorInHudGrace(panel, default, stale, stale, true, true, limit));
        Assert.True(ChaosHudWindow.CursorInHudGrace(panel, default, stale, stale, false, false, limit));
        Assert.False(ChaosHudWindow.CursorInHudGrace(panel, default, new Point(150, 50), stale, false, true, 0));
    }
}
