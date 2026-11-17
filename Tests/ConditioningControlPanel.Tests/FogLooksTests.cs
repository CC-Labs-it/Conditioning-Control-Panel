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
        Assert.Contains("Prizes.WornFog.Shown((look.R, look.G, look.B))", steam);
        Assert.Contains("Face, _t, mix, ramp);", steam);
        var tint = File.ReadAllText(Path.Combine(app, "Services", "Compositor", "PinkTintLayer.cs"));
        Assert.Contains("if (worn.TwoTone)", tint);
        Assert.Contains("WornFog.MixSpeed(MotionFx.Level)", tint);
        // The welcome show paints its own pink: it never wears a shop look.
        var show = File.ReadAllText(Path.Combine(app, "Services", "FirstShow", "FirstShowEffects.cs"));
        Assert.DoesNotContain("WearsLooks", show);
    }

    // ---- Two-tone ----------------------------------------------------------------------------

    [Fact]
    public void The_ramp_keeps_its_ends_and_never_goes_grey_or_dark_between_them()
    {
        var pink = ((byte)255, (byte)105, (byte)180);
        foreach (var other in new[] { FogLooks.Mint, FogLooks.Lilac, FogLooks.Amber })
        {
            var b = FogLooks.ColourOf(other)!.Value;
            var ramp = FogMix.Ramp(pink, b);
            Assert.Equal(FogMix.RampSize, ramp.Length);
            Assert.Equal(pink, ramp[0]);
            Assert.Equal(b, ramp[^1]);
            double floor = Math.Min(FogMix.Luma(pink), FogMix.Luma(b)) - 0.02;
            foreach (var c in ramp)
            {
                Assert.True(FogMix.Luma(c) >= floor, $"{other}: {c} darker than both ends");
                Assert.True(FogMix.ToHsv(c).S >= 0.3, $"{other}: {c} goes grey");
            }
        }
    }

    [Fact]
    public void Both_colours_always_show_and_the_mix_moves_but_holds_still_at_motion_off()
    {
        var field = new FogMixField();
        double total = 0; int frames = 0;
        for (double t = 0; t < 600; t += 7)
        {
            field.Fill(48, 27, 16 / 9.0, t);
            int second = 0;
            foreach (var s in field.S) if (FogMix.Shape(s) > 0.5) second++;
            double share = second / (double)field.S.Length;
            Assert.InRange(share, 0.05, 0.95);
            total += share; frames++;
        }
        Assert.InRange(total / frames, 0.25, 0.75);

        // A few seconds apart the picture plainly differs; the same moment is the same picture.
        Assert.Equal(FogMix.Field(0.4, 0.6, 1.7, 12), FogMix.Field(0.4, 0.6, 1.7, 12));
        double moved = 0;
        for (int i = 0; i < 20; i++) moved += Math.Abs(FogMix.Shape(FogMix.Field(i * 0.09, 0.5, 1.78, 12)) - FogMix.Shape(FogMix.Field(i * 0.09, 0.5, 1.78, 15)));
        Assert.True(moved > 1, $"moved {moved}");

        var clock = WornFog.Clock;
        try
        {
            double now = 50;
            WornFog.Clock = () => now;
            double t0 = WornFog.MixTime(0);
            now += 3;
            Assert.Equal(t0, WornFog.MixTime(0));
            now += 0.05;
            Assert.Equal(t0 + 0.025, WornFog.MixTime(WornFog.MixSpeed(ConditioningControlPanel.Models.MotionLevel.Reduced)), 6);
            Assert.Equal(0, WornFog.MixSpeed(ConditioningControlPanel.Models.MotionLevel.Off));
            Assert.Equal(1, WornFog.MixSpeed(ConditioningControlPanel.Models.MotionLevel.Full));
        }
        finally { WornFog.Clock = clock; }
    }

    [Fact]
    public void Two_tone_wears_the_second_colour_and_fades_in_and_out()
    {
        var read = WornFog.Read;
        var clock = WornFog.Clock;
        try
        {
            double now = 1000;
            bool mixOn = false;
            WornFog.Clock = () => now;
            WornFog.Read = slot => slot switch
            {
                WornLooks.Fog => FogLooks.Mint,
                WornLooks.Fog2 => FogLooks.Pink,
                WornLooks.Mix => mixOn ? "fx.fog.twotone" : null,
                _ => WornLooks.DefaultOf(slot),
            };
            WornFog.Retarget();
            now += 10;
            var flat = WornFog.Shown(Own);
            Assert.False(flat.TwoTone);
            Assert.Equal(FogLooks.ColourOf(FogLooks.Mint), flat.Main);

            mixOn = true;
            WornFog.Retarget();
            var start = WornFog.Shown(Own);
            Assert.True(start.TwoTone);
            Assert.Equal(start.Main, start.Second);                 // grows in from one colour
            now += 10;
            var on = WornFog.Shown(Own);
            Assert.True(on.TwoTone);
            Assert.Equal(Own, on.Second);                           // pink = the filter's own colour

            mixOn = false;
            WornFog.Retarget();
            now += 0.5;
            Assert.True(WornFog.Shown(Own).TwoTone);                // still fading out
            now += 10;
            Assert.False(WornFog.Shown(Own).TwoTone);
        }
        finally
        {
            WornFog.Read = read;
            WornFog.Clock = clock;
            WornFog.Retarget();
        }
    }

    [Fact]
    public void Nothing_worn_is_today_s_flat_colour()
    {
        var read = WornFog.Read;
        try
        {
            WornFog.Read = WornLooks.DefaultOf;
            WornFog.Retarget();
            WornFog.Clock = () => 1e6;
            var shown = WornFog.Shown(Own);
            Assert.Equal(new WornFog.FogShown(Own, Own, false), shown);
        }
        finally
        {
            WornFog.Read = read;
            WornFog.Clock = () => System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
            WornFog.Retarget();
        }
    }

    private static string Root()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "ConditioningControlPanel", "ConditioningControlPanel.csproj")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("repo root not found");
    }
}
