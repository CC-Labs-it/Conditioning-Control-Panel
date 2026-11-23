using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Looks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.SuperCore;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Accessibility "Limit pink filter to" (2026-11-23, a light-sensitivity ask): off changes nothing,
/// on holds the painted tint and the V2 fog to the limit whatever a preset, ramp or pulse asks for,
/// and the paint points (compositor tint, legacy tint windows, Steam fog) all go through the rule.
/// </summary>
public class PinkComfortLimitTests
{
    // ---- limit off = unchanged ------------------------------------------------------------

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.05)]
    [InlineData(0.10)]
    [InlineData(0.37)]
    [InlineData(0.50)]
    [InlineData(1.0)]
    public void Off_paints_what_was_asked(double asked)
    {
        Assert.Equal(asked, PinkComfort.Apply(asked, on: false, pct: 10), 9);
        Assert.Equal(asked, PinkComfort.FogStrength(asked, on: false, pct: 5), 9);
    }

    [Fact]
    public void Off_leaves_the_fog_curve_as_shipped()
    {
        for (int pct = 0; pct <= 100; pct++)
        {
            double s = SteamOptions.Strength(pct / 100.0);
            Assert.Equal(s, PinkComfort.FogStrength(s, false, PinkComfort.DefaultPct), 9);
        }
    }

    [Fact]
    public void Defaults_are_off_at_ten_percent()
    {
        var s = new AppSettings();
        Assert.False(s.PinkComfortLimitOn);
        Assert.Equal(10, s.PinkComfortLimitPct);
        Assert.Equal(1.0, PinkComfort.Cap(s.PinkComfortLimitOn, s.PinkComfortLimitPct), 9);
    }

    // ---- limit on = clamped ---------------------------------------------------------------

    [Theory]
    [InlineData(0.50, 10, 0.10)]
    [InlineData(0.11, 10, 0.10)]
    [InlineData(0.10, 10, 0.10)]
    [InlineData(0.05, 10, 0.05)]   // under the limit: untouched
    [InlineData(1.00, 25, 0.25)]
    [InlineData(0.40, 99, 0.40)]   // a limit over the slider's top reads as 50
    [InlineData(0.80, 99, 0.50)]
    [InlineData(0.40, 1, 0.05)]    // and under its bottom as 5
    public void On_holds_the_tint_to_the_limit(double asked, int pct, double painted)
        => Assert.Equal(painted, PinkComfort.Apply(asked, true, pct), 9);

    [Fact]
    public void On_holds_the_fog_to_the_limit_too()
    {
        // The fog is drawn thicker than the tint: at a 10% filter it paints about 0.46 as shipped.
        double shipped = SteamOptions.Strength(0.10);
        Assert.True(shipped > 0.4);
        double capped = PinkComfort.Apply(0.50, true, 10);
        Assert.Equal(0.10, PinkComfort.FogStrength(SteamOptions.Strength(capped), true, 10), 9);
        // A fog already thinner than the limit keeps its own thickness.
        double thin = SteamOptions.Strength(0.01);
        Assert.Equal(thin, PinkComfort.FogStrength(thin, true, 50), 9);
    }

    [Fact]
    public void Bad_numbers_never_paint_past_the_limit()
    {
        Assert.Equal(0.0, PinkComfort.Apply(double.NaN, true, 10), 9);
        Assert.Equal(0.10, PinkComfort.Apply(double.PositiveInfinity, true, 10), 9);
        Assert.Equal(0.0, PinkComfort.Apply(-1, true, 10), 9);
        Assert.Equal(0.10, PinkComfort.FogStrength(5, true, 10), 9);
    }

    [Fact]
    public void The_setting_clamps_to_the_slider_range()
    {
        var s = new AppSettings { PinkComfortLimitPct = 0 };
        Assert.Equal(PinkComfort.MinPct, s.PinkComfortLimitPct);
        s.PinkComfortLimitPct = 500;
        Assert.Equal(PinkComfort.MaxPct, s.PinkComfortLimitPct);
    }

    [Theory]
    [InlineData(10, true, 10, false)]
    [InlineData(11, true, 10, true)]
    [InlineData(50, false, 10, false)]
    [InlineData(30, true, 25, true)]
    public void The_slider_note_shows_only_while_clipping(int filterPct, bool on, int pct, bool clips)
        => Assert.Equal(clips, PinkComfort.Clips(filterPct, on, pct));

    // ---- presets and ramps ----------------------------------------------------------------

    [Fact]
    public void On_clamps_every_built_in_preset_and_a_strong_user_preset()
    {
        var presets = Preset.GetDefaultPresets().ToList();
        presets.Add(new Preset { Name = "strong", PinkFilterEnabled = true, PinkFilterOpacity = 50 });
        foreach (var p in presets)
        {
            var s = new AppSettings { PinkComfortLimitOn = true, PinkComfortLimitPct = 10 };
            p.ApplyTo(s);
            // A preset never carries the limit: it stays the player's own.
            Assert.True(s.PinkComfortLimitOn, $"preset {p.Name} switched the limit off");
            Assert.Equal(10, s.PinkComfortLimitPct);
            double painted = PinkComfort.Apply(s.PinkFilterOpacity / 100.0, s.PinkComfortLimitOn, s.PinkComfortLimitPct);
            Assert.True(painted <= 0.10 + 1e-9, $"preset {p.Name} paints {painted}");
            // Limit off: the preset's own value, untouched.
            Assert.Equal(s.PinkFilterOpacity / 100.0, PinkComfort.Apply(s.PinkFilterOpacity / 100.0, false, 10), 9);
        }
    }

    [Fact]
    public void A_preset_does_not_name_the_limit()
    {
        // Same rule as Gentle visuals: a global preference, never in Preset or SessionSettings.
        Assert.Null(typeof(Preset).GetProperty("PinkComfortLimitOn"));
        Assert.Null(typeof(Preset).GetProperty("PinkComfortLimitPct"));
        Assert.Null(typeof(SessionSettings).GetProperty("PinkComfortLimitOn"));
    }

    [Fact]
    public void On_clamps_a_ramp_a_pulse_and_a_deeper_band()
    {
        // The intensity ramp climbs to 50 (MainWindow.StartStop), the overlay pulse doubles to 100
        // (OverlayService.PulseOverlays), a Deeper band can ask for full opacity.
        foreach (double mult in new[] { 1.0, 1.5, 2.0, 3.0, 5.0 })
        {
            int ramped = (int)Math.Clamp(10 * mult, 0, 50);
            Assert.True(PinkComfort.Apply(ramped / 100.0, true, 10) <= 0.10 + 1e-9);
            Assert.Equal(ramped / 100.0, PinkComfort.Apply(ramped / 100.0, false, 10), 9);
        }
        double pulse = Math.Min(50 * 2, 100) / 100.0;
        Assert.Equal(0.15, PinkComfort.Apply(pulse, true, 15), 9);
        Assert.Equal(0.20, PinkComfort.Apply(1.0, true, 20), 9);
    }

    // ---- every paint point goes through the rule ------------------------------------------

    [Fact]
    public void The_compositor_tint_clips_where_it_paints()
    {
        var src = File.ReadAllText(Path.Combine(AppRoot(), "Services", "Compositor", "PinkTintLayer.cs"));
        Assert.Contains("double o = Safety.PinkComfortLimit.Apply(_opacity) * Super.SuperStepAside.Ease(_veil);", src);
        // A moved limit repaints a steady (dirty-gated) tint, and Steam reads the clipped look.
        Assert.Contains("if (cap != _paintedCap) { _paintedCap = cap; _dirty = true; }", src);
        Assert.Contains("Safety.PinkComfortLimit.Apply(l.Opacity)", src);
    }

    [Fact]
    public void The_fog_is_held_to_the_limit()
    {
        var src = File.ReadAllText(Path.Combine(AppRoot(), "Services", "Compositor", "SteamLayer.cs"));
        Assert.Contains("Safety.PinkComfortLimit.FogStrength(SteamOptions.Strength(look.Opacity))", src);
    }

    [Fact]
    public void Every_legacy_tint_window_alpha_goes_through_the_limit()
    {
        var src = File.ReadAllText(Path.Combine(AppRoot(), "Services", "Notifications", "OverlayService.cs"));
        // No raw "(byte)(x * 255)" alpha for a tint is left: each one is PinkAlpha(...).
        Assert.DoesNotMatch(new Regex(@"FromArgb\(\s*\(byte\)"), src);
        Assert.Contains("byte a = PinkAlpha(opacity);", src);
        Assert.Contains("var alpha = PinkAlpha(boosted / 100.0);", src);
        Assert.Contains("PinkAlpha(actualOpacity), fr, fg, fb)", src);
        Assert.Equal(2, Regex.Matches(src, @"PinkAlpha\(actualOpacity\)").Count);
    }

    private static string AppRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "ConditioningControlPanel");
    }
}
