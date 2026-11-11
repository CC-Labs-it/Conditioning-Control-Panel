using System;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Core.Tests;

public class ChaosEscortOrbitTests
{
    private static (AmbientBubbleField Field, AmbientBubble Live, AmbientBubble Escort) Pair()
    {
        var pair = ChaosBubbleVariants.BuildChaperonePair(0.5);
        var field = new AmbientBubbleField(new Random(1));
        var live = AmbientBubble.SpawnChaos(field.Random, 0, 0, 0, 1920, 1080, 1, pair.Live);
        var escort = AmbientBubble.SpawnChaos(field.Random, 0, 0, 0, 1920, 1080, 1, pair.Escort);
        live.AttachChaperone(escort, 0, 0, 1920, 1080, field.Random);
        field.Bubbles.Add(live); field.Bubbles.Add(escort);
        return (field, live, escort);
    }

    [Fact]
    public void Orbit_breathes_without_rotating_or_using_normal_motion()
    {
        var (field, _, escort) = Pair();
        double initial = escort.DrawScale, angle = escort.Angle;
        for (int i = 0; i < 5; i++) field.Step();
        Assert.NotEqual(initial, escort.DrawScale);
        Assert.Equal(angle, escort.Angle);
        Assert.False(escort.Popping);
    }

    [Fact]
    public void Resolving_live_dissolves_escort_in_place_without_spin()
    {
        var (field, live, escort) = Pair();
        live.Popping = true;
        field.Step();
        double x = escort.X, y = escort.Y, angle = escort.Angle;
        for (int i = 1; i <= 19; i++)
        {
            Assert.Equal(0, field.Step());
            Assert.Equal(1 - 0.02 * i, escort.Scale, 6);
            Assert.Equal(1 - 0.05 * i, escort.Fade, 6);
            Assert.Equal((x, y, angle), (escort.X, escort.Y, escort.Angle));
        }
        Assert.Contains(escort, field.Bubbles);
        Assert.Equal(0, field.Step());
        Assert.DoesNotContain(escort, field.Bubbles);
    }

    [Fact]
    public void Escort_clears_rims_orbits_and_dissolves_without_a_miss()
    {
        var pair = ChaosBubbleVariants.BuildChaperonePair(0.5);
        var field = new AmbientBubbleField(new Random(1));
        var live = AmbientBubble.SpawnChaos(field.Random, 0, 0, 0, 1920, 1080, 1, pair.Live);
        var escort = AmbientBubble.SpawnChaos(field.Random, 0, 0, 0, 1920, 1080, 1, pair.Escort);
        live.AttachChaperone(escort, 0, 0, 1920, 1080, field.Random);
        field.Bubbles.Add(live); field.Bubbles.Add(escort);
        live.FuseRemainingMs = int.MaxValue;
        double radius = AmbientBubble.EscortRadius(live.Size, escort.Size);
        Assert.True(radius >= (live.Size + escort.Size) / 2 + ChaosTuning.CHAPERONE_ORBIT_GAP_DIP);
        Assert.Equal(radius, escort.CenterX - live.CenterX, 6);
        int dots = 0;
        field.ChaosEscortShimmer = _ => dots++;
        for (int i = 0; i < 7; i++) field.Step();
        Assert.Equal(1, dots);
        double angle = 2 * Math.PI * (7 * 0.032) / 2.5;
        Assert.Equal(Math.Cos(angle) * radius, escort.CenterX - live.CenterX, 6);
        Assert.Equal(Math.Sin(angle) * radius, escort.CenterY - live.CenterY, 6);
        for (int i = 0; i < 200; i++)
        {
            field.Step();
            Assert.InRange(escort.CenterX, escort.Size / 2, 1920 - escort.Size / 2);
            Assert.InRange(escort.CenterY, escort.Size / 2, 1080 - escort.Size / 2);
        }
        live.Popping = true;
        Assert.Equal(0, field.Step());
        Assert.True(escort.Popping);
        for (int i = 0; i < 30; i++) Assert.Equal(0, field.Step());
        Assert.DoesNotContain(escort, field.Bubbles);
    }
}
