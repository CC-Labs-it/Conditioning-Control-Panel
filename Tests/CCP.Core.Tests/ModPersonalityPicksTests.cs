using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Companion;
using Newtonsoft.Json;
using Xunit;

namespace CCP.Core.Tests;

public sealed class ModPersonalityPicksTests
{
    [Fact]
    public void RememberedPicksRoundTripAndRestoreOnlyAnAvailableDifferentPick()
    {
        var settings = new AppSettings();
        ModPersonalityPicks.Store(settings.ModPersonalityPreset, "Builtin-Locked", "circe_soft");
        ModPersonalityPicks.Store(settings.ModPersonalityPreset, "builtin-drone", "drone");
        var saved = JsonConvert.DeserializeObject<AppSettings>(JsonConvert.SerializeObject(settings))!;
        Assert.Equal("circe_soft", ModPersonalityPicks.ForModSwitch(saved.ModPersonalityPreset,
            "builtin-locked", "drone", id => id == "circe_soft"));
        Assert.Null(ModPersonalityPicks.ForModSwitch(saved.ModPersonalityPreset,
            "builtin-locked", "circe_soft", id => true));
        Assert.Null(ModPersonalityPicks.ForModSwitch(saved.ModPersonalityPreset,
            "builtin-locked", "drone", id => false));
        Assert.Null(ModPersonalityPicks.ForModSwitch(saved.ModPersonalityPreset,
            "unknown-mod", "drone", id => true));
        Assert.Equal(2, saved.ModPersonalityPreset.Count);
    }

    [Theory]
    [InlineData(null, "pick")]
    [InlineData("", "pick")]
    [InlineData("mod", " ")]
    public void BlankIdsCannotCreateAnExplicitPick(string? mod, string? preset)
    {
        var map = new Dictionary<string, string>();
        ModPersonalityPicks.Store(map, mod, preset);
        Assert.Empty(map);
    }
}
