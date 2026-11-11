using ConditioningControlPanel.Services.Safety;
using Xunit;
using static ConditioningControlPanel.Services.Safety.PanicRouteGate;

namespace CCP.Core.Tests;

public sealed class PanicRouteGateTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_other_routes_echo_of_one_press_is_dropped(bool x11First)
    {
        var (first, echo) = x11First ? (Route.X11, Route.Portal) : (Route.Portal, Route.X11);
        var gate = new PanicRouteGate();
        Assert.True(gate.Admit(first, 1000));
        Assert.False(gate.Admit(echo, 1000 + EchoWindowMs - 1));
    }

    [Fact]
    public void A_route_that_went_away_cannot_echo()
    {
        var gate = new PanicRouteGate();
        Assert.True(gate.Admit(Route.Portal, 1000));
        gate.Forget();
        Assert.True(gate.Admit(Route.X11, 1100));
    }

    [Fact]
    public void A_real_double_press_on_one_route_is_never_dropped()
    {
        var gate = new PanicRouteGate();
        Assert.True(gate.Admit(Route.X11, 1000));
        Assert.True(gate.Admit(Route.X11, 1001));
    }

    [Fact]
    public void A_real_second_press_heard_by_both_routes_counts_once_more()
    {
        var gate = new PanicRouteGate();
        Assert.True(gate.Admit(Route.X11, 1000));
        Assert.False(gate.Admit(Route.Portal, 1001));
        Assert.True(gate.Admit(Route.X11, 1300));
        Assert.False(gate.Admit(Route.Portal, 1301));
    }

    [Fact]
    public void A_press_only_the_other_route_reports_is_not_taken_for_a_second_echo()
    {
        // The portal swallows a repeat when Deactivated was lost, so press two reaches only XInput2.
        var gate = new PanicRouteGate();
        Assert.True(gate.Admit(Route.Portal, 1000));
        Assert.False(gate.Admit(Route.X11, 1001));
        Assert.True(gate.Admit(Route.X11, 1300));
    }

    [Fact]
    public void The_other_route_is_heard_again_once_the_window_passes()
    {
        var gate = new PanicRouteGate();
        Assert.True(gate.Admit(Route.X11, 1000));
        Assert.True(gate.Admit(Route.Portal, 1000 + EchoWindowMs));
    }
}
