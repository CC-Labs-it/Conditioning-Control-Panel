using System;
using System.IO;
using ConditioningControlPanel.SuperCore;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Tier-2 round on 7.3.3 (owner 2026-11-18, "make all the interactions togglable"): the Whirlpool's
/// word catch joins its Drags row (on by default, as shipped), the fog's lowest reach is a thin rim
/// at the edges and its lowest opacity really is thin. The pops themselves are #5532's
/// "Moving effects pop" switch (MovingPopsSwitchTests).
/// </summary>
public class V2InteractionSwitchTests
{
    private static SuperStage LivePool()
    {
        var s = new SuperStage { PoolOn = true };
        for (double t = 0; t < 20; t += 1 / 30.0) s.Pool.Step(1 / 30.0, 500, 400, 0, 1, 1080);
        Assert.True(s.PoolLive);
        return s;
    }

    [Fact]
    public void Defaults_are_the_shipped_interactions()
    {
        var set = new Models.AppSettings();
        Assert.True(set.WhirlpoolDragsWords);
        Assert.True(new SuperStage().DragsWords);
    }

    [Fact]
    public void With_words_off_the_pool_neither_bends_nor_catches_a_word()
    {
        var s = LivePool();
        s.DragsWords = false;
        Assert.False(s.PoolTakesWords);
        var st = new FlingState();
        double x = 500 + s.Pool.Radius * 0.2, y = 400, vx = -200, vy = 0;
        for (int i = 0; i < 30; i++)
        {
            Assert.False(TextFling.Step(ref st, s, ref x, ref y, ref vx, ref vy, 200, 1 / 30.0));
            Assert.Equal(0, st.Phase);
            Assert.Equal(-200, vx, 6);          // untouched: no bend toward the eye
            Assert.Equal(0, vy, 6);
            x += vx / 30; y += vy / 30;
        }
    }

    [Fact]
    public void A_caught_word_is_let_go_when_words_are_switched_off()
    {
        var s = LivePool();
        var st = new FlingState();
        double x = 500 + s.Pool.Radius * 0.2, y = 400, vx = -200, vy = 0;
        for (int i = 0; i < 5; i++) TextFling.Step(ref st, s, ref x, ref y, ref vx, ref vy, 200, 1 / 30.0);
        Assert.Equal(1, st.Phase);
        s.DragsWords = false;
        Assert.True(TextFling.Step(ref st, s, ref x, ref y, ref vx, ref vy, 200, 1 / 30.0));
        Assert.Equal(2, st.Phase);
    }

    [Fact]
    public void The_lowest_reach_is_a_thin_rim_at_the_edges()
    {
        var f = new SteamField(1920, 1080) { Drift = 0 };
        f.Settle(SteamOptions.Level(1e6, 240, SteamOptions.ReachMin), 1);
        Assert.True(f.At(960, 2) > 0.2);         // the very edge has fog
        Assert.Equal(0, f.At(960, 120), 6);      // 120 px in (about a ninth of the height) is clear
        Assert.Equal(0, f.At(960, 540), 6);
        // The old bottom (0.555) still covered 120 px in; the new one never reaches past it.
        var old = new SteamField(1920, 1080) { Drift = 0 };
        old.Settle(0.45 + 1.05 * 0.10, 1);
        Assert.True(old.At(960, 120) > 0.5);
        // The full reach is the fog as shipped.
        Assert.Equal(SteamOptions.Level(1e6, 240), SteamOptions.Level(1e6, 240, SteamOptions.ReachMax), 9);
    }

    [Fact]
    public void Fog_strength_thins_below_the_default_opacity_and_keeps_the_shipped_curve_above()
    {
        Assert.Equal(0.46, SteamOptions.Strength(0.10), 9);              // the filter's default: as shipped
        Assert.Equal(0.62, SteamOptions.Strength(0.20), 9);
        Assert.Equal(0.82, SteamOptions.Strength(0.50), 9);              // never opaque
        Assert.Equal(0.23, SteamOptions.Strength(0.05), 9);              // the slider's bottom is half the default
        Assert.Equal(0, SteamOptions.Strength(0), 9);
        Assert.Equal(0.46, SteamOptions.Strength(double.NaN), 9);
        double last = -1;
        for (double o = 0; o <= 0.5; o += 0.01) { double v = SteamOptions.Strength(o); Assert.True(v >= last); last = v; }
    }

    [Fact]
    public void The_switches_sit_in_their_gold_boxes_and_read_in_ten_languages()
    {
        var root = Root();
        void InBox(string page, params string[] names)
        {
            var xaml = File.ReadAllText(Path.Combine(root, "Features", page));
            int box = xaml.IndexOf("<ctl:SuperBox", StringComparison.Ordinal), end = xaml.IndexOf("</ctl:SuperBox>", box, StringComparison.Ordinal);
            foreach (var n in names) Assert.InRange(xaml.IndexOf($"x:Name=\"{n}\"", StringComparison.Ordinal), box, end);
        }
        InBox("SpiralFeatureControl.xaml", "PoolRows", "ChkPoolDragsWords");
        var host = File.ReadAllText(Path.Combine(root, "Services", "Super", "SuperStageHost.cs"));
        Assert.Contains("Stage.DragsWords = set?.WhirlpoolDragsWords", host);
        var steam = File.ReadAllText(Path.Combine(root, "Services", "Compositor", "SteamLayer.cs"));
        Assert.Contains("SteamOptions.Strength(look.Opacity)", steam);

        var keys = new[] { "super_pool_drags_words", "tooltip_super_pool_drags" };
        var files = Directory.GetFiles(Path.Combine(root, "Localization", "Languages"), "*.json");
        Assert.Equal(10, files.Length);
        foreach (var file in files)
        {
            var json = JObject.Parse(File.ReadAllText(file));
            foreach (var key in keys)
            {
                var text = (string?)json[key];
                Assert.False(string.IsNullOrWhiteSpace(text), $"{Path.GetFileName(file)} {key}");
                Assert.DoesNotContain("!", text);
                Assert.DoesNotContain("—", text);
                Assert.DoesNotContain("–", text);
                Assert.False(LocalizationEncodingTests.LooksDoubleEncoded(text!), $"{Path.GetFileName(file)} {key}");
            }
        }
    }

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources"))) dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root");
        return Path.Combine(dir!.FullName, "ConditioningControlPanel");
    }
}
