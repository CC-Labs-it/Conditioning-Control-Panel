using System;
using System.IO;
using ConditioningControlPanel.Services.Prizes;
using ConditioningControlPanel.SuperCore;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The fog look from the shop (lane J2, owner notes 2026-11-17): only Two-tone is sold. Colour one is
/// always the user's own filter colour; the second comes from a fixed palette and steps on when it sits too
/// close to the user's own; nothing worn draws today's colour, and a change cross-fades instead of jumping.</summary>
public class FogLooksTests
{
    private static readonly (byte, byte, byte) Own = (255, 105, 180);

    [Fact]
    public void The_palette_is_the_mockup_s_plus_sky_and_peach_and_no_two_swirl_into_one()
    {
        Assert.Equal(((byte)0xA9, (byte)0x8B, (byte)0xFF), FogLooks.ColourOf(FogLooks.Lilac));
        Assert.Equal(((byte)0x5F, (byte)0xE0, (byte)0xB8), FogLooks.ColourOf(FogLooks.Mint));
        Assert.Equal(((byte)0xFF, (byte)0xB3, (byte)0x47), FogLooks.ColourOf(FogLooks.Amber));
        Assert.Equal(((byte)0xFF, (byte)0x5F, (byte)0xA8), FogLooks.ColourOf(FogLooks.Pink));
        Assert.Equal(new[] { FogLooks.Pink, FogLooks.Lilac, FogLooks.Mint, FogLooks.Amber, FogLooks.Sky, FogLooks.Peach },
            System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(FogLooks.Palette, p => p.Row)));
        foreach (var row in new[] { null, "fx.fog.bogus", FogLooks.Twotone, "" }) Assert.False(FogLooks.IsPalette(row));
        foreach (var a in FogLooks.Palette)
            foreach (var b in FogLooks.Palette)
                if (a.Row != b.Row) Assert.False(FogLooks.Close(a.Rgb, b.Rgb), $"{a.Row} and {b.Row}");
        Assert.Equal(FogLooks.Lilac, WornLooks.DefaultOf(WornLooks.Fog2));
    }

    [Theory]
    [InlineData("fx.fog.mint", 255, 105, 180, "fx.fog.mint")]
    [InlineData(null, 255, 105, 180, "fx.fog.lilac")]
    [InlineData("bogus", 255, 105, 180, "fx.fog.lilac")]
    [InlineData("fx.fog.amber", 255, 105, 180, "fx.fog.amber")]
    [InlineData("fx.fog.pink", 255, 105, 180, "fx.fog.lilac")]   // hot pink is too close to pink: the next one
    [InlineData("fx.fog.lilac", 160, 140, 250, "fx.fog.mint")]   // a lilac filter skips lilac
    [InlineData("fx.fog.peach", 255, 150, 120, "fx.fog.pink")]   // the last colour wraps round to the first
    public void The_second_colour_never_swirls_into_the_user_s_own(string? fog2, int r, int g, int b, string expected)
    {
        var own = ((byte)r, (byte)g, (byte)b);
        Assert.Equal(expected, FogLooks.SecondRow(fog2, own));
        Assert.False(FogLooks.Close(FogLooks.SecondColour(fog2, own), own));
    }

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
    public void A_worn_fog_colour_never_recolours_the_fog()
    {
        // An old preview (or account) still naming a fog colour in slot fog: the fog stays the user's own.
        var read = WornFog.Read;
        var clock = WornFog.Clock;
        try
        {
            double now = 100;
            WornFog.Clock = () => now;
            WornFog.Read = slot => slot == WornLooks.Fog ? FogLooks.Mint : WornLooks.DefaultOf(slot);
            WornFog.Retarget();
            now += 10;
            Assert.Equal(new WornFog.FogShown(Own, Own, false), WornFog.Shown(Own));
            Assert.Equal(((byte)10, (byte)200, (byte)90), WornFog.Shown((10, 200, 90)).Main);   // the user's pick, whatever it is
            Assert.False(WornFog.Easing);
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
        // The filter's colour is the user's own again: no shop colour reaches the window fallback.
        Assert.DoesNotContain("WornFog", overlay);
        Assert.DoesNotContain("OnWornLookChanged", overlay);
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
            string fog2 = FogLooks.Pink;
            WornFog.Read = slot => slot switch
            {
                WornLooks.Fog => FogLooks.Mint,                     // ignored: colour one is the user's own
                WornLooks.Fog2 => fog2,
                WornLooks.Mix => mixOn ? "fx.fog.twotone" : null,
                _ => WornLooks.DefaultOf(slot),
            };
            WornFog.Retarget();
            now += 10;
            var flat = WornFog.Shown(Own);
            Assert.False(flat.TwoTone);
            Assert.Equal(Own, flat.Main);

            mixOn = true;
            WornFog.Retarget();
            var start = WornFog.Shown(Own);
            Assert.True(start.TwoTone);
            Assert.Equal(start.Main, start.Second);                 // grows in from one colour
            now += 10;
            var on = WornFog.Shown(Own);
            Assert.True(on.TwoTone);
            Assert.Equal(Own, on.Main);
            Assert.Equal(FogLooks.ColourOf(FogLooks.Lilac), on.Second);   // pink is too close to hot pink: lilac stands in
            Assert.Equal(FogLooks.ColourOf(FogLooks.Pink), WornFog.Shown((95, 224, 184)).Second);   // a mint filter keeps pink

            long stamp = WornFog.Stamp;
            fog2 = FogLooks.Sky;                                    // a new second colour cross-fades
            WornFog.Retarget();
            Assert.NotEqual(stamp, WornFog.Stamp);
            Assert.True(WornFog.Easing);
            now += 10;
            Assert.False(WornFog.Easing);
            Assert.Equal(FogLooks.ColourOf(FogLooks.Sky), WornFog.Shown(Own).Second);

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
