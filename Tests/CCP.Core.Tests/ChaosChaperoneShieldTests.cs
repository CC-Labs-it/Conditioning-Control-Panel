using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Core.Tests;

public class ChaosChaperoneShieldTests
{
    private static (AmbientBubbleField Field, AmbientBubble Live, AmbientBubble Escort) Pair()
    {
        var field = new AmbientBubbleField();
        var live = new AmbientBubble { Spec = new EffectBubbleSpec { IsLive = true, IsChaperoneLive = true } };
        var escort = new AmbientBubble { Spec = new EffectBubbleSpec { IsEscort = true } };
        live.Escort = escort;
        field.Bubbles.Add(live);
        field.Bubbles.Add(escort);
        return (field, live, escort);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Press_is_swallowed_while_escort_stands(bool canChannel)
    {
        var (field, live, escort) = Pair();
        int detonations = 0;
        field.ChaosDetonated = _ => detonations++;
        field.PressLive(live, canChannel, 0, 0);
        Assert.False(live.Channeling);
        Assert.False(live.Popping);
        Assert.Equal(0, detonations);
        escort.Popping = true;
        field.PressLive(live, canChannel, 0, 0);
        Assert.Equal(canChannel, live.Channeling);
        Assert.Equal(!canChannel, live.Popping);
    }

    [Fact]
    public void Sweep_bounces_until_escort_pops_or_leaves_field()
    {
        var (field, live, escort) = Pair();
        int snaps = 0;
        field.ChaosDefused = _ => snaps++;
        field.DefuseAllLive();
        Assert.False(live.Popping);
        Assert.Equal(0, snaps);
        field.Bubbles.Remove(escort);
        field.DefuseAllLive();
        Assert.True(live.Deflating);
        Assert.Equal(1, snaps);
    }
}
