using System;
using System.IO;
using ConditioningControlPanel.SuperCore;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Steam's and the Whirlpool's gold-box options (owner, 2026-11-15): defaults reproduce the
/// shipped behaviour, clamps hold, each number reaches the rule it turns, the pool's "Drags"
/// switches gate fog, pictures and bubbles, and the rows sit in the gold box in ten languages.
/// </summary>
public class SteamWhirlpoolOptionsTests
{
    private static SuperStage LivePool()
    {
        var s = new SuperStage { PoolOn = true };
        for (double t = 0; t < 20; t += 1 / 30.0) s.Pool.Step(1 / 30.0, 500, 400, 0, 1, 1080);
        Assert.True(s.PoolLive);
        return s;
    }

    [Fact]
    public void Steam_defaults_are_the_shipped_fog()
    {
        Assert.Equal(240, SteamOptions.CreepSeconds(SteamOptions.Normal));
        foreach (var age in new[] { 0.0, 60, 120, 239, 400 })
            Assert.Equal(0.45 + 1.05 * Math.Clamp(age / 240.0, 0, 1), SteamOptions.Level(age, 240), 9);
        Assert.Equal(SteamField.HealRate, SteamOptions.HealRate(SteamOptions.ReturnDefault), 9);
        Assert.Equal(1.0, SteamOptions.Drift(SteamOptions.DriftDefault));
        var f = new SteamField(1920, 1080);
        Assert.Equal(SteamField.HealRate, f.Heal);
        Assert.Equal(1.0, f.Drift);

        var set = new Models.AppSettings();
        Assert.Equal(100, set.SteamFogReturn);
        Assert.Equal(100, set.SteamDrift);
        Assert.Equal(Services.Super.CreepSpeed.Normal, set.SuperCreepSpeed);
        // The app's creep speed enum and the Core picks line up (Slow, Normal, Fast).
        Assert.Equal(SteamOptions.Slow, (int)Services.Super.CreepSpeed.Slow);
        Assert.Equal(SteamOptions.Normal, (int)Services.Super.CreepSpeed.Normal);
        Assert.Equal(SteamOptions.Fast, (int)Services.Super.CreepSpeed.Fast);
    }

    [Fact]
    public void Steam_options_clamp_and_order()
    {
        Assert.True(SteamOptions.CreepSeconds(SteamOptions.Slow) > SteamOptions.CreepSeconds(SteamOptions.Normal));
        Assert.True(SteamOptions.CreepSeconds(SteamOptions.Fast) < SteamOptions.CreepSeconds(SteamOptions.Normal));
        Assert.Equal(1.5, SteamOptions.Level(1e6, 120), 9);
        var set = new Models.AppSettings { SteamFogReturn = 9999, SteamDrift = -5 };
        Assert.Equal(SteamOptions.ReturnMax, set.SteamFogReturn);
        Assert.Equal(0, set.SteamDrift);
        set.SteamFogReturn = 1;
        Assert.Equal(SteamOptions.ReturnMin, set.SteamFogReturn);
        // Even the fastest return closes a hole over more than a second: far above the Gentle floor.
        Assert.True(1 / SteamOptions.HealRate(SteamOptions.ReturnMax) > 1.0);
    }

    [Fact]
    public void Fog_returns_faster_with_a_higher_heal()
    {
        double After(double heal)
        {
            var f = new SteamField(1920, 1080) { Heal = heal, Drift = 0 };
            f.Settle(1.5, 1);
            f.Hole(960, 540, 200);
            for (int i = 0; i < 60; i++) f.Step(1 / 30.0, 1.5, 1);
            return f.At(960, 540);
        }
        Assert.True(After(SteamOptions.HealRate(300)) > After(SteamOptions.HealRate(100)) + 0.05);
        Assert.True(After(SteamOptions.HealRate(100)) > After(SteamOptions.HealRate(25)) + 0.02);
    }

    [Fact]
    public void Still_fog_does_not_drift_and_stock_fog_does()
    {
        float[] Run(double drift)
        {
            var f = new SteamField(1920, 1080) { Drift = drift };
            f.Settle(0.8, 1);
            for (int i = 0; i < 90; i++) f.Step(1 / 30.0, 0.8, 1);
            return (float[])f.Fog.Clone();
        }
        var settled = new SteamField(1920, 1080);
        settled.Settle(0.8, 1);
        double Moved(float[] a) { double d = 0; for (int i = 0; i < a.Length; i++) d += Math.Abs(a[i] - settled.Fog[i]); return d; }
        Assert.True(Moved(Run(0)) < 1e-3);
        Assert.True(Moved(Run(1)) > 0.5);
    }

    [Fact]
    public void Whirlpool_defaults_are_the_shipped_pool()
    {
        var set = new Models.AppSettings();
        Assert.True(set.WhirlpoolDragsFog && set.WhirlpoolDragsPictures && set.WhirlpoolDragsBubbles);
        Assert.Equal(100, set.WhirlpoolSize);
        Assert.Equal((int)LoomMorph.HoldSec, set.WhirlpoolNewEvery);
        Assert.Equal(1.0, WhirlpoolOptions.ReachScale(set.WhirlpoolSize));
        Assert.Equal(1.0, new Whirlpool().ReachScale);
        Assert.Equal(LoomMorph.HoldSec, new LoomMorph(new Random(1)).Hold);
        var s = new SuperStage();
        Assert.True(s.DragsFog && s.DragsPictures && s.DragsBubbles);
        set.WhirlpoolSize = 999; set.WhirlpoolNewEvery = 1;
        Assert.Equal(WhirlpoolOptions.SizeMax, set.WhirlpoolSize);
        Assert.Equal(WhirlpoolOptions.HoldMin, set.WhirlpoolNewEvery);
        Assert.True(WhirlpoolOptions.HoldMin > LoomMorph.MorphSec);
    }

    [Fact]
    public void Pool_size_scales_the_reach()
    {
        var s = LivePool();
        double stock = s.Pool.Radius;
        s.Pool.ReachScale = WhirlpoolOptions.ReachScale(150);
        Assert.Equal(stock * 1.5, s.Pool.Radius, 6);
        s.Pool.ReachScale = WhirlpoolOptions.ReachScale(50);
        Assert.Equal(stock * 0.5, s.Pool.Radius, 6);
    }

    [Fact]
    public void New_spiral_every_sets_when_the_next_one_rolls_in()
    {
        var m = new LoomMorph(new Random(3)) { Hold = 8 };
        for (int i = 0; i < 8 * 30 - 5; i++) m.Step(1 / 30.0);
        Assert.Null(m.To);
        for (int i = 0; i < 10; i++) m.Step(1 / 30.0);
        Assert.NotNull(m.To);
        // A held spiral stays, whatever the hold.
        var held = new LoomMorph(new Random(3)) { Hold = 8 };
        held.Pin(new LoomSpec());
        for (int i = 0; i < 60 * 30; i++) held.Step(1 / 30.0);
        Assert.Null(held.To);
    }

    [Fact]
    public void Drags_switches_gate_pictures_bubbles_and_fog()
    {
        var s = LivePool();
        double x = s.Pool.X + 60, y = s.Pool.Y;
        Assert.Equal(PullKind.Picture, WhirlpoolOptions.KindOf(WhirlpoolOptions.PictureMass));
        Assert.Equal(PullKind.Bubble, WhirlpoolOptions.KindOf(1));
        Assert.True(s.Pull(x, y, WhirlpoolOptions.PictureMass).Inside);
        Assert.True(s.Pull(x, y, 1).Inside);

        s.DragsPictures = false;
        Assert.False(s.Pull(x, y, WhirlpoolOptions.PictureMass).Inside);
        Assert.False(s.Pull(x, y, 1, PullKind.Picture).Inside);
        Assert.True(s.Pull(x, y, 1).Inside);

        s.DragsPictures = true; s.DragsBubbles = false;
        Assert.False(s.Pull(x, y, 0.8).Inside);
        Assert.True(s.Pull(x, y, WhirlpoolOptions.PictureMass).Inside);

        // Fog: with DragsFog off the pool leaves the fog exactly as the plain step leaves it.
        float[] Fog(bool drags)
        {
            var st = LivePool();
            st.SteamOn = true; st.DragsFog = drags;
            var f = st.Field("m", 0, 0, 1920, 1080);
            f.Drift = 0;
            f.Settle(1.5, 1);
            for (int i = 0; i < 30; i++) st.StepSteam(1 / 30.0, 1.5, 1);
            return f.Fog;
        }
        var plain = new SteamField(1920, 1080) { Drift = 0 };
        plain.Settle(1.5, 1);
        for (int i = 0; i < 30; i++) plain.Step(1 / 30.0, 1.5, 1);
        double Diff(float[] a) { double d = 0; for (int i = 0; i < a.Length; i++) d += Math.Abs(a[i] - plain.Fog[i]); return d; }
        Assert.True(Diff(Fog(false)) < 1e-3);
        Assert.True(Diff(Fog(true)) > 0.5);
    }

    [Fact]
    public void The_rows_sit_in_the_gold_boxes_and_read_in_ten_languages()
    {
        var root = Root();
        void InBox(string page, params string[] names)
        {
            var xaml = File.ReadAllText(Path.Combine(root, "Features", page));
            int box = xaml.IndexOf("<ctl:SuperBox", StringComparison.Ordinal), end = xaml.IndexOf("</ctl:SuperBox>", StringComparison.Ordinal);
            Assert.Contains("#33FFE08A", xaml.Substring(box, end - box));
            foreach (var n in names) Assert.InRange(xaml.IndexOf($"x:Name=\"{n}\"", StringComparison.Ordinal), box, end);
        }
        InBox("PinkFilterFeatureControl.xaml", "SteamRows", "ChkSteamWords", "CmbCreepSpeed", "SliderSteamSize", "SliderSteamReturn", "SliderSteamDrift");
        InBox("SpiralFeatureControl.xaml", "PoolRows", "CmbPoolSpiral", "SliderPoolEvery", "SliderPoolSize",
            "ChkPoolDragsFog", "ChkPoolDragsPictures", "ChkPoolDragsBubbles");

        var keys = new[]
        {
            "super_steam_creep", "tooltip_super_steam_creep", "super_steam_return", "tooltip_super_steam_return",
            "super_steam_drift", "tooltip_super_steam_drift", "super_pool_every", "super_pool_every_value",
            "tooltip_super_pool_every", "super_pool_size", "tooltip_super_pool_size", "super_pool_drags",
            "tooltip_super_pool_drags", "super_pool_drags_fog", "super_pool_drags_pictures", "super_pool_drags_bubbles",
        };
        var files = Directory.GetFiles(Path.Combine(root, "..", "CCP.Core", "Localization", "Languages"), "*.json");
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
            Assert.Contains("{0}", (string)json["super_pool_every_value"]!);
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
