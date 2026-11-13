using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Perk effects: Full / No cards / Off (owner, 2026-11-05). Cards show only in Full, visuals in
/// everything but Off, and the old SuppressPerkNotifications bool migrates to No cards.
/// </summary>
public class PerkEffectsModeTests
{
    [Theory]
    [InlineData(PerkEffectsMode.Full, true, true)]
    [InlineData(PerkEffectsMode.NoCards, false, true)]
    [InlineData(PerkEffectsMode.Off, false, false)]
    public void EachModeShowsWhatItSays(PerkEffectsMode mode, bool cards, bool visuals)
    {
        Assert.Equal(cards, PerkEffectsRules.ShowsCards(mode));
        Assert.Equal(visuals, PerkEffectsRules.ShowsVisuals(mode));
    }

    [Theory]
    [InlineData(0, PerkEffectsMode.Full)]
    [InlineData(1, PerkEffectsMode.NoCards)]
    [InlineData(2, PerkEffectsMode.Off)]
    [InlineData(-1, PerkEffectsMode.Full)]
    [InlineData(7, PerkEffectsMode.Full)]
    public void IndexMapsToModeAndUnknownReadsFull(int index, PerkEffectsMode expected)
        => Assert.Equal(expected, PerkEffectsRules.FromIndex(index));

    [Fact]
    public void NewSettingsDefaultToFull()
    {
        var s = new AppSettings();
        Assert.Equal(PerkEffectsMode.Full, s.PerkEffects);
        Assert.False(s.SuppressPerkNotifications);
    }

    [Fact]
    public void AnOldFileWithTheBoolOnLoadsAsNoCards()
    {
        var s = JsonConvert.DeserializeObject<AppSettings>("{\"SuppressPerkNotifications\": true}")!;
        Assert.Equal(PerkEffectsMode.NoCards, s.PerkEffects);
    }

    [Fact]
    public void AnOldFileWithTheBoolOffLoadsAsFull()
    {
        var s = JsonConvert.DeserializeObject<AppSettings>("{\"SuppressPerkNotifications\": false}")!;
        Assert.Equal(PerkEffectsMode.Full, s.PerkEffects);
    }

    [Theory]
    [InlineData("{\"PerkEffects\": 2, \"SuppressPerkNotifications\": true}")]
    [InlineData("{\"SuppressPerkNotifications\": true, \"PerkEffects\": 2}")]
    public void OffSurvivesTheLegacyFieldInEitherOrder(string json)
    {
        var s = JsonConvert.DeserializeObject<AppSettings>(json)!;
        Assert.Equal(PerkEffectsMode.Off, s.PerkEffects);
    }

    [Theory]
    [InlineData(PerkEffectsMode.Full)]
    [InlineData(PerkEffectsMode.NoCards)]
    [InlineData(PerkEffectsMode.Off)]
    public void EveryModeRoundTripsAndKeepsTheLegacyBoolForOlderBuilds(PerkEffectsMode mode)
    {
        var json = JsonConvert.SerializeObject(new AppSettings { PerkEffects = mode });
        var back = JsonConvert.DeserializeObject<AppSettings>(json)!;
        Assert.Equal(mode, back.PerkEffects);
        // An older build reads only the bool: anything but Full keeps its cards off.
        Assert.Equal(mode != PerkEffectsMode.Full, back.SuppressPerkNotifications);
    }

    [Fact]
    public void TheLegacySetterNeverLowersAStrongerChoice()
    {
        Assert.Equal(PerkEffectsMode.Off, PerkEffectsRules.FromLegacySuppress(PerkEffectsMode.Off, true));
        Assert.Equal(PerkEffectsMode.Off, PerkEffectsRules.FromLegacySuppress(PerkEffectsMode.Off, false));
        Assert.Equal(PerkEffectsMode.NoCards, PerkEffectsRules.FromLegacySuppress(PerkEffectsMode.NoCards, false));
    }
}
