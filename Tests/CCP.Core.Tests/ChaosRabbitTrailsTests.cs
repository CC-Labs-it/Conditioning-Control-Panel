using System;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Core.Tests;

public class ChaosRabbitTrailsTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static (double X, double Y)? Step(ChaosRabbitTrails t, double x, double scale = 1,
        double life = 2, bool sweeper = false, bool telegraph = false, bool skia = false, DateTime? now = null) =>
        t.Step(x, 0, scale, life, sweeper, telegraph, false, skia, now ?? Now);

    [Fact]
    public void Spacing_scales_with_dpi_and_skia_is_denser()
    {
        var t = new ChaosRabbitTrails();
        Assert.Null(Step(t, 79, scale: 2));
        Assert.Equal((80d, 0d), Step(t, 80, scale: 2));
        Assert.Null(Step(t, 123, scale: 2, skia: true));
        Assert.Equal((44d, 0d), Step(t, 124, scale: 2, skia: true));
    }

    [Fact]
    public void Points_age_out_and_are_capped_at_sixty()
    {
        var t = new ChaosRabbitTrails();
        for (int i = 1; i <= 61; i++) Step(t, i * 40, life: 100);
        Assert.Equal(60, t.Points.Count);
        Assert.Equal(80, t.Points[0].X);
        Step(t, 2440, life: 2, now: Now.AddSeconds(2));
        Assert.Equal(60, t.Points.Count);
        Step(t, 2440, life: 2, now: Now.AddSeconds(2.01));
        Assert.Empty(t.Points);
    }

    [Fact]
    public void Telegraph_and_disabled_clear_points_but_sweepers_have_half_second_minimum()
    {
        var t = new ChaosRabbitTrails();
        Assert.Null(Step(t, 40, telegraph: true));
        Assert.Null(Step(t, 40, life: 0));
        Assert.NotNull(Step(t, 40, life: 0, sweeper: true));
        Assert.Single(t.Points);
        Step(t, 40, life: 0, sweeper: true, now: Now.AddSeconds(.51));
        Assert.Empty(t.Points);
        Step(t, 80);
        Step(t, 120, telegraph: true);
        Assert.Empty(t.Points);
    }
}
