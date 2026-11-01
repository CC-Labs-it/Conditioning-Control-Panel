using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Companion;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Owner call 2026-10-30: "Rain" is only the 3D model's name. Player-facing copy calls the V2 avatar by
/// the mod's chosen name (Bambi, Bimbo, or Emi on CCP Default and everywhere else) through the
/// <c>{companion}</c> loc token. Ids, grants and settings keep "rain".
/// </summary>
public class CompanionNameTests
{
    /// <summary>All ten language files.</summary>
    private static readonly string[] Languages = { "en", "de", "es", "fr", "it", "ja", "ko", "pt-BR", "ru", "zh-CN" };

    [Theory]
    [InlineData(BuiltInMods.BambiSleepId, "Bambi")]
    [InlineData(BuiltInMods.SissyHypnoId, "Bimbo")]
    [InlineData(BuiltInMods.CCPDefaultId, "Emi")]
    [InlineData(BuiltInMods.LockedId, "Emi")]
    [InlineData(BuiltInMods.DronificationId, "Emi")]
    [InlineData("some-custom-mod", "Emi")]
    [InlineData("", "Emi")]
    [InlineData(null, "Emi")]
    public void ForMod_NamesEachMod_AndFallsBackToEmi(string? modId, string expected)
        => Assert.Equal(expected, CompanionName.ForMod(modId));

    [Fact]
    public void ForMod_IgnoresCase()
        => Assert.Equal("Bambi", CompanionName.ForMod(BuiltInMods.BambiSleepId.ToUpperInvariant()));

    [Fact]
    public void NoModLayer_IsEmi()
    {
        // Tests run with no ModService: the answer is the fallback, never a throw.
        Assert.Equal(CompanionName.Fallback, CompanionName.ForActiveMod());
        Assert.Equal("Emi", CompanionName.Fallback);
    }

    [Fact]
    public void Token_IsFilledByTheLocPass()
    {
        Assert.Equal("Emi's wardrobe", VocabTokens.Apply("{companion}'s wardrobe"));
        Assert.Equal("V2: Emi, Emi.", VocabTokens.Apply("V2: {companion}, {companion}."));
        Assert.Equal("{Companion}", VocabTokens.Apply("{Companion}"));   // case-sensitive like the others
    }

    [Fact]
    public void V2AvatarCopy_NeverSaysRain_AndUsesTheToken()
    {
        var avatarKey = new Regex(@"^(tube_pill_v[12]_tip|section_rain_wardrobe|rain_.+|companion_hero_wardrobe|help_counter_.+|help_rain_.+|emi_book_prize_counter_.+|emi_book_rain_.+|shop_tab_rain_.+|shop_item_rain_.+|shop_item_profile_avatar_rain_.+)$");
        var rain = new Regex(@"(?<![A-Za-z])Rain");
        foreach (var lang in Languages)
        {
            var json = JObject.Parse(File.ReadAllText(LangPath(lang)));
            var keys = json.Properties().Where(p => avatarKey.IsMatch(p.Name)).ToList();
            Assert.NotEmpty(keys);
            foreach (var p in keys)
                Assert.False(rain.IsMatch((string?)p.Value ?? ""), $"{lang} {p.Name} still names the model: {p.Value}");
            foreach (var key in new[] { "tube_pill_v2_tip", "rain_wardrobe_title", "help_rain_title", "shop_tab_rain_clothes" })
                Assert.Contains(VocabTokens.CompanionToken, (string?)json[key] ?? "");
        }
    }

    private static string LangPath(string lang)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return Path.Combine(dir!.FullName, "ConditioningControlPanel", "Localization", "Languages", lang + ".json");
    }
}
