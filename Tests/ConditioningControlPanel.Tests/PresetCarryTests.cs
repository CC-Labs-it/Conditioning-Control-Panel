using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1695 #1696 (7.3.6): presets never stored Steam, the filter colour, floating videos, flash
/// motion or any V2 option, so white Steam saved in one preset showed in every other one. A preset now
/// carries them (<see cref="PresetCarry"/>); one without them puts the looks back to their defaults
/// (owner, 2026-11-20) and leaves the monitor targets alone.
/// </summary>
public class PresetCarryTests
{
    private static PropertyInfo P(string name) => typeof(AppSettings).GetProperty(name)!;
    private static IEnumerable<string> All => PresetCarry.Looks.Concat(PresetCarry.Screens);

    /// <summary>A value of the property's type that differs from <paramref name="v"/>.</summary>
    private static object Other(Type t, object? v)
    {
        if (t == typeof(bool)) return !(bool)v!;
        if (t == typeof(int)) return (int)v! + 1;
        if (t == typeof(double)) return Math.Round((double)v! / 2 + 0.05, 3);
        if (t == typeof(string)) return (string?)v == "#FFFFFF" ? "#000000" : "#FFFFFF";
        if (t.IsEnum) return Enum.GetValues(t).Cast<object>().First(x => !x.Equals(v));
        throw new InvalidOperationException(t.Name);
    }

    private static AppSettings Changed()
    {
        var s = new AppSettings();
        foreach (var name in All) P(name).SetValue(s, Other(P(name).PropertyType, P(name).GetValue(s)));
        return s;
    }

    private static void AssertSame(AppSettings want, AppSettings got, IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            object? a = P(name).GetValue(want), b = P(name).GetValue(got);
            // Some setters snap a double to their step; a hair of float noise is the same setting.
            if (a is double da && b is double db) Assert.True(Math.Abs(da - db) < 1e-9, name);
            else Assert.True(Equals(a, b), name);
        }
    }

    [Fact]
    public void Every_name_is_a_writable_scalar_setting_listed_once()
    {
        var names = All.ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        foreach (var name in names)
        {
            var p = P(name);
            Assert.True(p != null && p.CanRead && p.CanWrite, name);
            var t = p!.PropertyType;
            Assert.True(t == typeof(bool) || t == typeof(int) || t == typeof(double) || t == typeof(string) || t.IsEnum, name + " " + t.Name);
            // Not already in the fixed field list (two writers would fight).
            Assert.Null(typeof(Preset).GetProperty(name));
        }
    }

    [Fact]
    public void The_reported_settings_are_carried()
    {
        foreach (var name in new[] { "PinkFilterColor", "SuperCreepCap", "SteamDrift", "MandatoryVideoFloating", "MandatoryVideoFloatingSpeedPercent", "FlashDriftSpeed" })
            Assert.Contains(name, PresetCarry.Looks);
        Assert.Contains("VideoTargetMonitor", PresetCarry.Screens);
    }

    [Fact]
    public void Save_then_load_restores_every_carried_setting()
    {
        var saved = Changed();
        var p = Preset.FromSettings(saved, "Bubble Bath");

        var live = new AppSettings();
        p.ApplyTo(live);

        AssertSame(saved, live, All);
    }

    [Fact]
    public void Old_preset_puts_the_looks_back_and_leaves_the_monitors()
    {
        var live = Changed();
        var old = Preset.FromSettings(live, "old");
        old.Carried = null; // saved before 7.3.7

        old.ApplyTo(live);

        AssertSame(new AppSettings(), live, PresetCarry.Looks);
        AssertSame(Changed(), live, PresetCarry.Screens);
    }

    [Fact]
    public void Built_in_preset_no_longer_inherits_white_steam()
    {
        var live = new AppSettings { PinkFilterColor = "#FFFFFF", SuperCreepCap = 0.5 };
        Preset.GetDefaultPresets().First(x => x.Id == "default-deep").ApplyTo(live);
        Assert.Equal(new AppSettings().PinkFilterColor, live.PinkFilterColor);
        Assert.Equal(new AppSettings().SuperCreepCap, live.SuperCreepCap);
    }

    [Fact]
    public void Settings_json_round_trip_keeps_them()
    {
        var saved = Changed();
        var holder = new AppSettings { UserPresets = new List<Preset> { Preset.FromSettings(saved, "p") } };
        var back = JsonConvert.DeserializeObject<AppSettings>(JsonConvert.SerializeObject(holder))!;

        var live = new AppSettings();
        back.UserPresets.Single().ApplyTo(live);
        AssertSame(saved, live, All);
    }

    [Fact]
    public void Exported_preset_file_round_trip_keeps_them()
    {
        var saved = Changed();
        var svc = new PresetFileService();
        var path = Path.Combine(Path.GetTempPath(), "ccp-carry-" + Guid.NewGuid().ToString("N") + ".preset.json");
        try
        {
            svc.ExportPreset(Preset.FromSettings(saved, "p"), path);
            var back = svc.ImportPreset(path);
            Assert.NotNull(back);

            var live = new AppSettings();
            back!.ApplyTo(live);
            AssertSame(saved, live, All);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Unreadable_values_are_skipped_not_thrown()
    {
        var live = new AppSettings { SteamReach = 42, FlashDriftSpeed = 0.7 };
        var p = Preset.FromSettings(live, "p");
        p.Carried!["SteamReach"] = "lots";
        p.Carried["FlashDriftSpeed"] = "NaN";
        p.Carried["SuperCreepSpeed"] = "7"; // numbers never pick an enum value
        p.Carried["NotASetting"] = "1";

        var target = new AppSettings { SteamReach = 30, FlashDriftSpeed = 0.5 };
        var speedBefore = target.SuperCreepSpeed;
        p.ApplyTo(target);

        Assert.Equal(30, target.SteamReach);
        Assert.Equal(0.5, target.FlashDriftSpeed, 9);
        Assert.Equal(speedBefore, target.SuperCreepSpeed);
    }

    [Fact]
    public void Doubles_survive_another_culture()
    {
        var prev = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("it-IT");
            var saved = new AppSettings { SuperCreepCap = 0.55, AfterglowLineHold = 2.5 };
            var p = Preset.FromSettings(saved, "p");
            Assert.Equal("0.55", p.Carried!["SuperCreepCap"]);

            var live = new AppSettings();
            p.ApplyTo(live);
            Assert.Equal(0.55, live.SuperCreepCap);
            Assert.Equal(saved.AfterglowLineHold, live.AfterglowLineHold, 9);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = prev; }
    }
}
