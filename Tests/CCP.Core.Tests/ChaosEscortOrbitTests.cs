using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ConditioningControlPanel.Localization;
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
    public void Escort_breathes_on_each_orbit_step()
    {
        var live = new AmbientBubble { Size = 240 };
        var escort = new AmbientBubble { Size = 100, Spec = new EffectBubbleSpec { IsEscort = true }, OrbitTarget = live };
        var before = escort.DrawScale;
        escort.Step();
        Assert.Equal(1 + 0.06 * Math.Sin(0.02 * 7.5), escort.DrawScale, 8);
        Assert.NotEqual(before, escort.DrawScale);
        escort.Step();
        Assert.Equal(1 + 0.06 * Math.Sin(0.04 * 7.5), escort.DrawScale, 8);
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

    [Theory]
    [InlineData(0, 0, 1920, 1080, 1)]
    [InlineData(-1600, 150, 1600, 900, 1.25)]
    public void Chaperone_roams_with_entire_escort_inside_work_area(double ax, double ay, double w, double h, double scale)
    {
        var random = new Random(2);
        var live = AmbientBubble.SpawnChaos(random, 0, ax, ay, w, h, scale,
            new EffectBubbleSpec { IsLive = true, IsChaperoneLive = true, SizePx = 220, FuseMs = 100000 });
        var escort = new AmbientBubble { Size = 100, Spec = new EffectBubbleSpec { IsEscort = true }, OrbitTarget = live };
        live.InsetChaperoneBounds(escort.Size);
        double x = live.X, y = live.Y;
        for (int i = 0; i < 500; i++)
        {
            live.Step(); escort.Step();
            Assert.InRange(escort.X, ax / scale - 0.001, (ax + w) / scale - escort.Size + 0.001);
            Assert.InRange(escort.Y, ay / scale - 0.001, (ay + h) / scale - escort.Size + 0.001);
            Assert.InRange(live.X, ax / scale, (ax + w) / scale - live.Size);
            Assert.InRange(live.Y, ay / scale, (ay + h) / scale - live.Size);
        }
        Assert.True(live.X != x || live.Y != y);
    }

    [Fact]
    public void Shield_flash_decays_and_cue_is_throttled()
    {
        var bubble = new AmbientBubble();
        var now = new DateTime(2026, 1, 1);
        Assert.True(bubble.BounceShield(now));
        Assert.Equal(0.95, bubble.ShieldOpacity);
        Assert.False(bubble.BounceShield(now.AddMilliseconds(100)));
        Assert.True(bubble.BounceShield(now.AddMilliseconds(200)));
        for (int i = 0; i < 10; i++) bubble.Step();
        Assert.InRange(bubble.ShieldOpacity, 0.35, 0.55);
    }

    [Fact]
    public void Chaperone_copy_is_present_in_every_language()
    {
        foreach (var (code, _, _) in LocalizationManager.AvailableLanguages)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Localization", "Languages", code + ".json");
            var strings = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;
            foreach (var key in new[] { "chaos_chaperone_title", "chaos_chaperone_hint", "chaos_chaperone_event" })
                Assert.True(strings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value), code + ": " + key);
        }
    }

}
