using System;
using System.IO;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Core.Tests;

public sealed class EmiCodexRulesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ccp-codex-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(false, false, 0, false)]
    [InlineData(false, true, 0, false)]
    [InlineData(false, false, 1, false)]
    [InlineData(false, true, 1, false)]
    [InlineData(true, false, 0, true)]
    [InlineData(true, true, 0, false)]
    [InlineData(true, false, 1, false)]
    [InlineData(true, true, 1, false)]
    public void EachOfferBrakeRefusesIndependently(bool content, bool open, int opens, bool allowed)
        => Assert.Equal(allowed, EmiCodexRules.CanOffer(content, open, () => opens));

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void ContentAndOpenBrakesDoNotReadTheProfile(bool content, bool open)
        => Assert.False(EmiCodexRules.CanOffer(content, open,
            () => throw new InvalidOperationException("state must not be read")));

    [Fact]
    public void MissingAndEmptyBundlesHaveNoContent()
    {
        Assert.False(EmiCodexRules.HasContent(null));
        Assert.False(EmiCodexRules.HasContent(_root));
        Directory.CreateDirectory(Path.Combine(_root, "chapters"));
        Assert.False(EmiCodexRules.HasContent(_root));
        File.WriteAllText(Path.Combine(_root, "chapters", "notes.txt"), "not a chapter");
        Assert.False(EmiCodexRules.HasContent(_root));
    }

    [Fact]
    public void HostedIndexCountsWithoutChapters()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "index.html"), "<html></html>");
        Assert.True(EmiCodexRules.BundlePresent(_root));
        Assert.True(EmiCodexRules.HasContent(_root));
    }

    [Fact]
    public void PlainChapterCountsWithoutHostedIndexAndIsReprobed()
    {
        var chapters = Path.Combine(_root, "chapters");
        Directory.CreateDirectory(chapters);
        var chapter = Path.Combine(chapters, "intro.json");
        File.WriteAllText(chapter, "malformed, as allowed by the WPF content probe");
        Assert.False(EmiCodexRules.BundlePresent(_root));
        Assert.True(EmiCodexRules.HasContent(_root));
        Assert.Empty(EmiCodexChapters.Read(chapters));
        File.Delete(chapter);
        Assert.False(EmiCodexRules.HasContent(_root));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
