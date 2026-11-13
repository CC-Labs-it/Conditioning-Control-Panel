using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using ConditioningControlPanel.Services.Mods;
using Xunit;

namespace CCP.Core.Tests;

public sealed class EmoteSetValidationTests
{
    private static void InFolder(Action<string> test)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-emote-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try { test(dir); }
        finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData("{idleRotation:[{clip:'only'}]}", "only")]
    [InlineData("{talking:{pool:['only']}}", "only")]
    [InlineData("{talking:{short:'only'}}", "only")]
    [InlineData("{talking:{medium:['only']}}", "only")]
    [InlineData("{talking:{long:'only'}}", "only")]
    [InlineData("{stemPrefix:{x:'only'}}", "only")]
    [InlineData("{mood:{x:'only'}}", "only")]
    [InlineData("{clickEmotes:['only']}", "only")]
    [InlineData("{expressive:{pool:'only'}}", "only")]
    [InlineData("{}", "idle")]
    public void EveryReferencedClipIsRequired(string map, string missing) => InFolder(dir =>
    {
        File.WriteAllText(Path.Combine(dir, "emotes.json"), map);
        if (missing != "idle") File.WriteAllText(Path.Combine(dir, "idle.gif"), "");
        var result = EmoteSetValidation.Inspect(dir);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Key == "mod_emote_clips_missing" && e.Arg!.Contains(missing + ".gif"));
        File.WriteAllText(Path.Combine(dir, missing + ".gif"), "");
        Assert.True(EmoteSetValidation.Inspect(dir).IsValid);
    });

    [Fact]
    public void WarningsDoNotInvalidateAndSizeCountsNestedFiles() => InFolder(dir =>
    {
        File.WriteAllText(Path.Combine(dir, "emotes.json"), "{}");
        File.WriteAllText(Path.Combine(dir, "idle.gif"), "");
        File.WriteAllText(Path.Combine(dir, "extra.gif"), "");
        Directory.CreateDirectory(Path.Combine(dir, "nested"));
        using (var file = File.Create(Path.Combine(dir, "nested", "large"))) file.SetLength(EmoteSetValidation.SoftLimitBytes + 1);
        var result = EmoteSetValidation.Inspect(dir);
        Assert.True(result.IsValid);
        Assert.Equal(2, result.GifCount);
        Assert.Contains(result.Warnings, w => w.Key == "mod_emote_large");
        Assert.Contains(result.Warnings, w => w.Key == "mod_emote_unused" && w.Arg!.Contains("extra"));
        Assert.Contains(result.Warnings, w => w.Key == "mod_emote_no_timing");
        Assert.Contains(result.Warnings, w => w.Key == "mod_emote_no_talk");
    });

    [Fact]
    public void EmoteMessagesAndPlaceholdersExistInEveryLanguage()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Localization", "Languages");
        var files = Directory.GetFiles(directory, "*.json");
        Assert.Equal(10, files.Length);
        var english = JObject.Parse(File.ReadAllText(Path.Combine(directory, "en.json")));
        var keys = english.Properties().Select(p => p.Name).Where(k => k.StartsWith("mod_emote_", StringComparison.Ordinal)).ToArray();
        Assert.Equal(20, keys.Length);
        foreach (var file in files)
        {
            var translations = JObject.Parse(File.ReadAllText(file));
            foreach (var key in keys)
            {
                var value = translations.Value<string>(key);
                Assert.False(string.IsNullOrWhiteSpace(value), $"{file}: {key}");
                var expected = Regex.Matches(english.Value<string>(key)!, @"\{\d+(?::[^}]+)?\}").Select(m => m.Value).OrderBy(x => x);
                var actual = Regex.Matches(value!, @"\{\d+(?::[^}]+)?\}").Select(m => m.Value).OrderBy(x => x);
                Assert.Equal(expected, actual);
            }
        }
    }

    [Fact]
    public void UppercaseGifExtensionMatchesOnLinux() => InFolder(dir =>
    {
        File.WriteAllText(Path.Combine(dir, "emotes.json"), "{}");
        File.WriteAllText(Path.Combine(dir, "IDLE.GIF"), "");
        Assert.True(EmoteSetValidation.Inspect(dir).IsValid);
    });

    [Fact]
    public void MissingOrMalformedMapIsInvalid() => InFolder(dir =>
    {
        var missing = EmoteSetValidation.Inspect(dir);
        Assert.False(missing.IsValid);
        Assert.Contains(missing.Errors, e => e.Key == "mod_emote_map_missing");
        File.WriteAllText(Path.Combine(dir, "emotes.json"), "[");
        var malformed = EmoteSetValidation.Inspect(dir);
        Assert.False(malformed.IsValid);
        Assert.Contains(malformed.Errors, e => e.Key == "mod_emote_map_invalid" && !string.IsNullOrEmpty(e.Arg));
        var absent = EmoteSetValidation.Inspect(null);
        Assert.False(absent.IsValid);
        Assert.Contains(absent.Errors, e => e.Key == "mod_emote_folder_missing");
    });
}
