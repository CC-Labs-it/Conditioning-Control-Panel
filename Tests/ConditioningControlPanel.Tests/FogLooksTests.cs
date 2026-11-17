using System;
using System.IO;
using ConditioningControlPanel.Services.Prizes;
using ConditioningControlPanel.SuperCore;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Fog colours from the shop (lane J2): the worn row recolours Steam and the pink filter,
/// nothing worn draws today's colour, and a change cross-fades instead of jumping.</summary>
public class FogLooksTests
{
    private static readonly (byte, byte, byte) Own = (255, 105, 180);

    [Fact]
    public void The_colours_are_the_mockup_s_and_pink_is_the_filter_s_own()
    {
        Assert.Equal(((byte)0xA9, (byte)0x8B, (byte)0xFF), FogLooks.ColourOf(FogLooks.Lilac));
        Assert.Equal(((byte)0x5F, (byte)0xE0, (byte)0xB8), FogLooks.ColourOf(FogLooks.Mint));
        Assert.Equal(((byte)0xFF, (byte)0xB3, (byte)0x47), FogLooks.ColourOf(FogLooks.Amber));
        foreach (var row in new[] { FogLooks.Pink, null, "fx.fog.bogus", "fx.fog.twotone", "" })
        {
            Assert.Null(FogLooks.ColourOf(row));
            Assert.Equal(Own, FogLooks.Wear(row, Own));
        }
        // A user's own pick stays theirs under the free row.
        Assert.Equal(((byte)10, (byte)20, (byte)30), FogLooks.Wear(WornLooks.DefaultOf(WornLooks.Fog), (10, 20, 30)));
    }

    [Theory]
    [InlineData("fx.fog.mint", "fx.fog.amber", "fx.fog.amber")]
    [InlineData("fx.fog.pink", "fx.fog.lilac", "fx.fog.lilac")]
    [InlineData("fx.fog.lilac", "fx.fog.lilac", "fx.fog.pink")]
    [InlineData("fx.fog.pink", "fx.fog.pink", "fx.fog.lilac")]
    [InlineData(null, null, "fx.fog.lilac")]
    [InlineData("fx.fog.amber", "bogus", "fx.fog.lilac")]
    public void The_mix_never_draws_one_colour(string? fog, string? fog2, string expected)
        => Assert.Equal(expected, FogLooks.SecondRow(fog, fog2));

    [Fact]
    public void The_ease_is_smooth_and_slower_under_gentle()
    {
        Assert.Equal(0, FogLooks.EaseAt(0, 1.2));
        Assert.Equal(1, FogLooks.EaseAt(1.2, 1.2));
        Assert.InRange(FogLooks.EaseAt(0.6, 1.2), 0.49, 0.51);
        Assert.True(FogLooks.EaseAt(0.1, 1.2) < 0.1 / 1.2);
        Assert.Equal(1.2, FogLooks.EaseSeconds(false, 2));
        Assert.Equal(2, FogLooks.EaseSeconds(true, 2));
        Assert.Equal(1.2, FogLooks.EaseSeconds(true, 0.25));
    }

    [Fact]
    public void A_worn_colour_cross_fades_in_and_the_default_is_untouched()
    {
        var read = WornFog.Read;
        var clock = WornFog.Clock;
        try
        {
            double now = 100;
            string? fog = null;
            WornFog.Clock = () => now;
            WornFog.Read = slot => slot == WornLooks.Fog ? fog ?? WornLooks.DefaultOf(slot) : WornLooks.DefaultOf(slot);
            WornFog.Retarget();
            now += 10;
            Assert.Equal(Own, WornFog.Over(Own));
            Assert.False(WornFog.Easing);

            long stamp = WornFog.Stamp;
            fog = FogLooks.Mint;
            WornFog.Retarget();
            Assert.NotEqual(stamp, WornFog.Stamp);
            Assert.True(WornFog.Easing);
            Assert.Equal(Own, WornFog.Over(Own));                       // starts from what was shown
            Assert.Equal(FogLooks.ColourOf(FogLooks.Mint), WornFog.Target(Own));
            now += 0.6;
            var mid = WornFog.Over(Own);
            Assert.NotEqual(Own, mid);
            Assert.NotEqual(FogLooks.ColourOf(FogLooks.Mint)!.Value, mid);
            now += 5;
            Assert.False(WornFog.Easing);
            Assert.Equal(FogLooks.ColourOf(FogLooks.Mint), WornFog.Over(Own));

            // Taking it off fades back to the filter's own colour; the same row again is no change.
            fog = null;
            WornFog.Retarget();
            stamp = WornFog.Stamp;
            WornFog.Retarget();
            Assert.Equal(stamp, WornFog.Stamp);
            now += 5;
            Assert.Equal(Own, WornFog.Over(Own));
        }
        finally
        {
            WornFog.Read = read;
            WornFog.Clock = clock;
            WornFog.Retarget();
        }
    }

    [Fact]
    public void Only_the_overlay_s_own_tint_wears_the_look_and_steam_asks_for_it()
    {
        string app = Path.Combine(Root(), "ConditioningControlPanel");
        var overlay = File.ReadAllText(Path.Combine(app, "Services", "Notifications", "OverlayService.cs"));
        Assert.Contains("new Compositor.PinkTintLayer(App.Compositor!) { WearsLooks = true }", overlay);
        Assert.Contains("Prizes.WornLooks.Changed += OnWornLookChanged;", overlay);
        Assert.Contains("Prizes.WornLooks.Changed -= OnWornLookChanged;", overlay);
        var steam = File.ReadAllText(Path.Combine(app, "Services", "Compositor", "SteamLayer.cs"));
        Assert.Contains("Prizes.WornFog.Over((look.R, look.G, look.B))", steam);
        // The welcome show paints its own pink: it never wears a shop look.
        var show = File.ReadAllText(Path.Combine(app, "Services", "FirstShow", "FirstShowEffects.cs"));
        Assert.DoesNotContain("WearsLooks", show);
    }

    private static string Root()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "ConditioningControlPanel", "ConditioningControlPanel.csproj")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("repo root not found");
    }
}
