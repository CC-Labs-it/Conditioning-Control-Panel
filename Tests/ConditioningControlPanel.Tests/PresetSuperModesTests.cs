using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Super;
using Newtonsoft.Json;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// A preset carries each effect's V2 switch (owner, 2026-11-16). A save writes all eight; an apply
/// sets only what the preset names; a preset saved before this (no map) leaves every switch alone.
/// </summary>
public class PresetSuperModesTests
{
    private static SuperMode Mode(AppSettings s, SuperEffect e) => SuperModeRule.Resolve(e, s.SuperModes);

    [Fact]
    public void Save_writes_every_effect_including_ones_never_touched()
    {
        var s = new AppSettings();
        s.SuperModes = SuperModeRule.With(s.SuperModes, SuperEffect.FlickerDeck, SuperMode.Classic);

        var p = Preset.FromSettings(s, "p");

        Assert.NotNull(p.SuperModes);
        foreach (var e in Enum.GetValues<SuperEffect>())
            Assert.True(p.SuperModes!.ContainsKey(e.ToString()), e + " missing");
        Assert.Equal((int)SuperMode.Classic, p.SuperModes!["FlickerDeck"]);
        // Never touched = the default (on), written out so a later default change cannot move it.
        Assert.True(SuperModeRule.RunsSuper((SuperMode)p.SuperModes["Creep"]));
    }

    [Fact]
    public void Apply_restores_the_saved_switches()
    {
        var saved = new AppSettings();
        saved.SuperModes = SuperModeRule.With(saved.SuperModes, SuperEffect.Vortex, SuperMode.Classic);
        saved.SuperModes = SuperModeRule.With(saved.SuperModes, SuperEffect.Scrawl, SuperMode.Classic);
        var p = Preset.FromSettings(saved, "p");

        var live = new AppSettings();
        live.SuperModes = SuperModeRule.With(live.SuperModes, SuperEffect.InnerBloom, SuperMode.Classic);
        p.ApplyTo(live);

        Assert.Equal(SuperMode.Classic, Mode(live, SuperEffect.Vortex));
        Assert.Equal(SuperMode.Classic, Mode(live, SuperEffect.Scrawl));
        // The preset had Inner Bloom on, so applying it turns it back on.
        Assert.True(SuperModeRule.RunsSuper(Mode(live, SuperEffect.InnerBloom)));
        foreach (var e in Enum.GetValues<SuperEffect>())
            Assert.Equal(Mode(saved, e), Mode(live, e));
    }

    [Fact]
    public void Old_preset_without_the_map_keeps_the_current_switches()
    {
        var live = new AppSettings();
        live.SuperModes = SuperModeRule.With(live.SuperModes, SuperEffect.Creep, SuperMode.Classic);
        var before = new Dictionary<string, int>(live.SuperModes);

        // A preset as 7.3.0 and older wrote it: no SuperModes field at all.
        var old = JsonConvert.DeserializeObject<Preset>("{\"Name\":\"Old\",\"FlashEnabled\":true}")!;
        Assert.Null(old.SuperModes);

        bool raised = false;
        live.PropertyChanged += (_, e) => raised |= e.PropertyName == nameof(AppSettings.SuperModes);
        old.ApplyTo(live);

        Assert.False(raised);
        Assert.Equal(before, live.SuperModes);
        Assert.Equal(SuperMode.Classic, Mode(live, SuperEffect.Creep));
    }

    [Fact]
    public void Built_in_presets_leave_the_switches_alone()
    {
        foreach (var p in Preset.GetDefaultPresets())
            Assert.Null(p.SuperModes);
    }

    [Fact]
    public void Apply_touches_only_the_effects_the_preset_names()
    {
        var live = new AppSettings();
        live.SuperModes = SuperModeRule.With(live.SuperModes, SuperEffect.LightsDown, SuperMode.Classic);
        var p = new Preset { SuperModes = new Dictionary<string, int> { ["Afterglow"] = (int)SuperMode.Classic, ["NotAnEffect"] = 0, ["3"] = 0 } };

        p.ApplyTo(live);

        Assert.Equal(SuperMode.Classic, Mode(live, SuperEffect.Afterglow));
        Assert.Equal(SuperMode.Classic, Mode(live, SuperEffect.LightsDown));
        Assert.True(SuperModeRule.RunsSuper(Mode(live, SuperEffect.Vortex)));
        Assert.False(live.SuperModes.ContainsKey("NotAnEffect"));
        Assert.False(live.SuperModes.ContainsKey("3"));
    }

    [Fact]
    public void Apply_matching_the_live_state_raises_nothing()
    {
        var live = new AppSettings();
        var p = Preset.FromSettings(live, "same");
        bool raised = false;
        live.PropertyChanged += (_, e) => raised |= e.PropertyName == nameof(AppSettings.SuperModes);

        p.ApplyTo(live);

        Assert.False(raised);
    }

    [Fact]
    public void Json_round_trip_keeps_the_switches()
    {
        var s = new AppSettings();
        s.SuperModes = SuperModeRule.With(s.SuperModes, SuperEffect.Undertow, SuperMode.Classic);
        var json = JsonConvert.SerializeObject(Preset.FromSettings(s, "rt"));

        var back = JsonConvert.DeserializeObject<Preset>(json)!;
        var live = new AppSettings();
        back.ApplyTo(live);

        Assert.Equal(SuperMode.Classic, Mode(live, SuperEffect.Undertow));
        Assert.True(SuperModeRule.RunsSuper(Mode(live, SuperEffect.FlickerDeck)));
    }

    [Fact]
    public void Merge_clamps_super_only_on_an_effect_that_cannot_replace()
    {
        var next = Preset.MergeSuperModes(
            new Dictionary<string, int> { ["FlickerDeck"] = 0 },
            new Dictionary<string, int> { ["FlickerDeck"] = (int)SuperMode.SuperOnly });

        Assert.NotNull(next);
        Assert.Equal((int)SuperMode.Both, next!["FlickerDeck"]);
    }
}
