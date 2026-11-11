using System;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Core.Tests;

public sealed class ChaosEscortOrbitTests
{
    [Fact]
    public void Escort_orbits_and_dissolves_when_live_resolves_or_disappears()
    {
        var field = new AmbientBubbleField(new Random(1));
        var live = new AmbientBubble { X = 400, Y = 300, Size = 240, Spec = new EffectBubbleSpec { IsLive = true } };
        var escort = new AmbientBubble { Size = 100, Spec = new EffectBubbleSpec { IsEscort = true }, OrbitTarget = live };
        live.Escort = escort;
        field.Bubbles.Add(live);
        field.Bubbles.Add(escort);
        escort.Step();
        double x = escort.CenterX, y = escort.CenterY;
        Assert.True(Math.Sqrt(Math.Pow(x - live.CenterX, 2) + Math.Pow(y - live.CenterY, 2)) >=
            Math.Max(ChaosTuning.CHAPERONE_ORBIT_RADIUS_DIP, (live.Size + escort.Size) / 2 + ChaosTuning.CHAPERONE_ORBIT_GAP_DIP) - 0.000001);
        escort.Step();
        Assert.True(x != escort.CenterX || y != escort.CenterY);
        live.Popping = true;
        field.Step();
        Assert.True(escort.Popping && escort.Deflating);

        var orphan = new AmbientBubble { Spec = new EffectBubbleSpec { IsEscort = true }, OrbitTarget = live };
        field.Bubbles.Add(orphan);
        field.Bubbles.Remove(live);
        field.Step();
        Assert.True(orphan.Popping && orphan.Deflating);
    }
    [Fact]
    public void Snap_field_respects_a_standing_escort()
    {
        var field = new AmbientBubbleField();
        var live = new AmbientBubble { Spec = new EffectBubbleSpec { IsLive = true, IsChaperoneLive = true } };
        var escort = new AmbientBubble { Spec = new EffectBubbleSpec { IsEscort = true } };
        live.Escort = escort;
        field.Bubbles.Add(live);
        field.Bubbles.Add(escort);
        field.DefuseAllLive();
        Assert.False(live.Popping);
        escort.Popping = true;
        field.DefuseAllLive();
        Assert.True(live.Popping);
    }

}
