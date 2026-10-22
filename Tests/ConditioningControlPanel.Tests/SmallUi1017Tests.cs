using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Fix wave 2026-10-17, small UI lane: Bubble Count says why it skipped (#1449), plus the copy
/// fixes (Slut Mode is not a perk, tiers are named as tiers, no bare "Chaos run").
/// </summary>
public class SmallUi1017Tests
{
    // ------------------------------------------------------------------ #1449 Bubble Count

    [Fact]
    public void NoVideos_PlayNowButton_AlwaysSaysWhy()
    {
        Assert.True(BubbleCountNoVideos.ShouldTell(forceTest: true, alreadyToldThisLaunch: false));
        Assert.True(BubbleCountNoVideos.ShouldTell(forceTest: true, alreadyToldThisLaunch: true));
    }

    [Fact]
    public void NoVideos_ScheduledRound_SaysWhyOncePerLaunch()
    {
        Assert.True(BubbleCountNoVideos.ShouldTell(forceTest: false, alreadyToldThisLaunch: false));
        Assert.False(BubbleCountNoVideos.ShouldTell(forceTest: false, alreadyToldThisLaunch: true));
    }

    [Theory]
    [InlineData(0, 0, "bubblecount_no_videos")]
    [InlineData(2, 1, "bubblecount_no_videos")]
    [InlineData(3, 0, "bubblecount_no_videos_packs_off")]
    public void NoVideos_PicksTheRightLine(int installed, int active, string key)
    {
        Assert.Equal(key, BubbleCountNoVideos.MessageKey(installed, active));
    }

    [Fact]
    public void NoVideos_TheServiceTellsThePlayer()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "ConditioningControlPanel", "Services", "BubbleCountService.cs"));
        int empty = source.IndexOf("App.Logger?.Warning(\"BubbleCountService: No videos found\");", StringComparison.Ordinal);
        Assert.True(empty >= 0);
        int ret = source.IndexOf("return;", empty, StringComparison.Ordinal);
        Assert.Contains("TellNoVideos(forceTest);", source.Substring(empty, ret - empty));
    }

    [Theory]
    [InlineData("bubblecount_no_videos")]
    [InlineData("bubblecount_no_videos_packs_off")]
    public void NoVideos_LineIsInEveryLanguage(string key)
    {
        foreach (var file in LanguageFiles())
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            Assert.True(doc.RootElement.TryGetProperty(key, out var v), Path.GetFileName(file) + " misses " + key);
            Assert.False(string.IsNullOrWhiteSpace(v.GetString()));
        }
    }

    // ------------------------------------------------------------------------- copy fixes

    /// <summary>Slut Mode is not gated, so the Patreon perk line must not sell it.</summary>
    [Fact]
    public void PatreonPerkLine_DoesNotSellSlutMode()
    {
        Assert.DoesNotContain("Slut", ReadKey("en.json", "label_patreon_features"), StringComparison.OrdinalIgnoreCase);
        foreach (var file in LanguageFiles())
        {
            var line = ReadKey(Path.GetFileName(file), "label_patreon_features");
            Assert.Equal(2, line.Count(c => c == ',' || c == '、'));
            Assert.EndsWith("{0}", line);
        }
    }

    [Fact]
    public void NoPlayerFacingChaosRun()
    {
        foreach (var file in LanguageFiles())
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("Chaos run", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Chaos Mode bubbles", text, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("vaultgate_title_vault")]
    [InlineData("premium_group_basic")]
    [InlineData("intake_pass_card_footnote")]
    public void BasicIsNamedAsATier(string key)
    {
        Assert.Contains("Basic tier", ReadKey("en.json", key), StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------- helpers

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }
    }

    private static string LanguagesDir =>
        Path.Combine(RepoRoot, "ConditioningControlPanel", "Localization", "Languages");

    private static string[] LanguageFiles()
    {
        var files = Directory.GetFiles(LanguagesDir, "*.json");
        Assert.Equal(10, files.Length);
        return files;
    }

    private static string ReadKey(string fileName, string key)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(LanguagesDir, fileName)));
        return doc.RootElement.GetProperty(key).GetString() ?? string.Empty;
    }
}
