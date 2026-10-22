using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The tube hides a visible EMI and brings her back after, unless the user dismissed her meanwhile.</summary>
public class TubeDeskVisibilityTests
{
    [Fact]
    public void Suppress_WhileVisible_HidesThenRestores()
    {
        var v = new TubeDeskVisibility();
        Assert.Equal((true, false), v.Update(suppress: true, visible: true));
        Assert.True(v.Suppressed);
        Assert.Equal((false, true), v.Update(suppress: false, visible: false));
        Assert.False(v.Suppressed);
    }

    [Fact]
    public void Suppress_WhileHidden_NeverRestores()
    {
        var v = new TubeDeskVisibility();
        Assert.Equal((false, false), v.Update(true, visible: false));
        Assert.Equal((false, false), v.Update(false, visible: false));
    }

    [Fact]
    public void Dismiss_DuringSuppression_CancelsTheRestore()
    {
        var v = new TubeDeskVisibility();
        v.Update(true, visible: true);
        v.Dismiss();
        Assert.Equal((false, false), v.Update(false, visible: false));
    }

    [Fact]
    public void RepeatedState_IsANoOp()
    {
        var v = new TubeDeskVisibility();
        v.Update(true, visible: true);
        Assert.Equal((false, false), v.Update(true, visible: true));
        Assert.Equal((false, true), v.Update(false, visible: false));
        Assert.Equal((false, false), v.Update(false, visible: true));
    }
}
