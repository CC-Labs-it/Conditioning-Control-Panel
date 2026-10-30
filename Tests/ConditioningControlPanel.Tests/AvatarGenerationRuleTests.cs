using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Companion;
using Newtonsoft.Json;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The V1/V2 avatar switch (Rain, handoff-1030/RAIN-V2-PLAN.md 2.1, lane R1): one global setting,
/// V2 only where the registry has a v2 row, today's answer everywhere else.
/// </summary>
public class AvatarGenerationRuleTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string App(string rel) => Path.Combine(RepoRoot(), "ConditioningControlPanel", rel);

    [Theory]
    // generation, mod has a v2 row, expected
    [InlineData("v1", true, "avatar0_emotes")]
    [InlineData("v1", false, "avatar0_emotes")]
    [InlineData("v2", true, "rain_emotes")]
    [InlineData("v2", false, "avatar0_emotes")]
    [InlineData(null, true, "avatar0_emotes")]
    [InlineData("V2 ", true, "rain_emotes")]
    [InlineData("v3", true, "avatar0_emotes")]
    public void ResolveFolder_V2OnlyWithARow(string? generation, bool hasRow, string expected)
    {
        var folder = AvatarGenerationRule.ResolveFolder(generation, hasRow ? "rain_emotes" : null, () => "avatar0_emotes");
        Assert.Equal(expected, folder);
    }

    [Fact]
    public void ResolveFolder_V2NeverAsksForTheV1Answer()
    {
        bool asked = false;
        var folder = AvatarGenerationRule.ResolveFolder("v2", "rain_emotes", () => { asked = true; return "avatar0_emotes"; });
        Assert.Equal("rain_emotes", folder);
        Assert.False(asked);
    }

    [Fact]
    public void ResolveFolder_PassesAnAbsolutePackFolderThrough()
    {
        var pack = Path.Combine(Path.GetTempPath(), "rain-packs", "abc123");
        Assert.Equal(pack, AvatarGenerationRule.ResolveFolder("v2", pack, () => "avatar0_emotes"));
        // V1 keeps today's answer, absolute (a mod-local set folder) or null (no emote set).
        Assert.Equal(pack, AvatarGenerationRule.ResolveFolder("v1", "rain_emotes", () => pack));
        Assert.Null(AvatarGenerationRule.ResolveFolder("v1", null, () => null));
    }

    [Fact]
    public void Toggle_And_Normalize()
    {
        Assert.Equal("v2", AvatarGenerationRule.Toggle("v1"));
        Assert.Equal("v1", AvatarGenerationRule.Toggle("v2"));
        Assert.Equal("v2", AvatarGenerationRule.Toggle(null));
        Assert.Equal("v1", AvatarGenerationRule.Normalize(""));
        Assert.Equal("v2", AvatarGenerationRule.Normalize("V2"));
    }

    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(false, false, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    public void PillVisible_OnlyWithARow_OutOfPortrait_NoGlitch(bool hasRow, bool portrait, bool glitch, bool expected)
        => Assert.Equal(expected, AvatarGenerationRule.PillVisible(hasRow, portrait, glitch));

    [Fact]
    public void Registry_KeepsTheOldSetRows_AndReadsTheV2RowsApart()
    {
        var (sets, generations) = AvatarGenerationRule.ParseRegistry(File.ReadAllText(App("Resources/avatar_emotes_registry.json")));

        Assert.Equal(new[] { 1, 2, 3, 4 }, sets.Where(s => s.modId == BuiltInMods.LockedId).Select(s => s.set).OrderBy(x => x));
        foreach (var mod in new[] { BuiltInMods.BambiSleepId, BuiltInMods.SissyHypnoId, BuiltInMods.CCPDefaultId })
        {
            Assert.Equal(new[] { (mod, 1, "avatar0_emotes") }, sets.Where(s => s.modId == mod).ToArray());
            Assert.Equal(new[] { (mod, "v2", "rain_emotes") }, generations.Where(g => g.modId == mod).ToArray());
        }
        // Locked's Circe has no V2; a generation row is never an avatar set.
        Assert.DoesNotContain(generations, g => g.modId == BuiltInMods.LockedId);
        Assert.DoesNotContain(sets, s => s.folder == "rain_emotes");
    }

    [Fact]
    public void Registry_SkipsIncompleteRows()
    {
        var (sets, generations) = AvatarGenerationRule.ParseRegistry(
            "{\"sets\":[{\"modId\":\"m\",\"avatarSet\":1,\"folder\":\"a\"},{\"modId\":\"m\",\"folder\":\"b\"}," +
            "{\"avatarSet\":2,\"folder\":\"c\"},{\"modId\":\"m\",\"generation\":\"v2\"},{\"modId\":\"m\",\"generation\":\"v2\",\"folder\":\"r\"}]}");
        Assert.Equal(new[] { ("m", 1, "a") }, sets.ToArray());
        Assert.Equal(new[] { ("m", "v2", "r") }, generations.ToArray());
    }

    [Fact]
    public void Setting_DefaultsToV1_AndRidesTheSettingsJson()
    {
        Assert.Equal("v1", new AppSettings().AvatarGeneration);

        var s = new AppSettings { AvatarGeneration = "v2" };
        var json = JsonConvert.SerializeObject(s);
        Assert.Contains("\"AvatarGeneration\":\"v2\"", json);
        Assert.Equal("v2", JsonConvert.DeserializeObject<AppSettings>(json)!.AvatarGeneration);

        // Anything unknown reads as V1 rather than a dead switch.
        Assert.Equal("v1", new AppSettings { AvatarGeneration = "rain" }.AvatarGeneration);
    }

    [Fact]
    public void Pill_IsNeverADragStart_AndTheArrowsStayAsTheyWere()
    {
        var windowing = File.ReadAllText(App("AvatarTube/AvatarTubeWindow.Windowing.cs"));
        Assert.Contains("IsDescendantOf(hit, BtnAvatarGeneration)) return;", windowing);

        var pill = File.ReadAllText(App("AvatarTube/AvatarTubeWindow.GenerationPill.cs"));
        Assert.Contains("e.Handled = true;", pill);
        Assert.DoesNotContain("EmoteSetsForActiveMod()", pill);
        Assert.DoesNotContain("BtnPrevAvatar.", pill);
        // The "Rain's wardrobe" tube menu door follows the switch.
        Assert.Contains("UpdateQuickMenuState();", pill);

        var xaml = File.ReadAllText(App("AvatarTube/AvatarTubeWindow.xaml"));
        Assert.Contains("x:Name=\"BtnAvatarGeneration\"", xaml);
    }
}
