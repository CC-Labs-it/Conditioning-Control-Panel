using System;
using System.IO;
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
        Assert.Contains(result.Errors, e => e.Contains(missing + ".gif"));
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
        Assert.Contains(result.Warnings, w => w.Contains("50 MB"));
        Assert.Contains(result.Warnings, w => w.Contains("extra"));
        Assert.Contains(result.Warnings, w => w.Contains("clip_timing"));
        Assert.Contains(result.Warnings, w => w.Contains("talkA"));
    });

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
        Assert.False(EmoteSetValidation.Inspect(dir).IsValid);
        File.WriteAllText(Path.Combine(dir, "emotes.json"), "[");
        Assert.False(EmoteSetValidation.Inspect(dir).IsValid);
        Assert.False(EmoteSetValidation.Inspect(null).IsValid);
    });
}
