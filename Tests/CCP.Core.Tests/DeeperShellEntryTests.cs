using System;
using System.IO;
using System.Text.Json;
using ConditioningControlPanel;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using Xunit;

namespace CCP.Core.Tests;

[Collection(SessionStatics.Name)]
public sealed class DeeperShellEntryTests
{
    // WPF DeeperTabView.xaml:480 uses this key; inspect each shipped file before fallback can hide a gap.
    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("it")]
    [InlineData("ja")]
    [InlineData("ko")]
    [InlineData("pt-BR")]
    [InlineData("ru")]
    [InlineData("zh-CN")]
    public void WelcomeCardBodyExistsInEveryShippedLanguage(string language)
    {
        const string key = "deeper_welcome_card_body";
        var folder = Path.Combine(AppContext.BaseDirectory, "Localization", "Languages");
        using var translation = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, language + ".json")));
        Assert.True(translation.RootElement.TryGetProperty(key, out var value), $"{language} lacks {key}");
        var text = value.GetString();
        Assert.False(string.IsNullOrWhiteSpace(text));
        if (language == "en") return;
        using var english = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "en.json")));
        Assert.NotEqual(english.RootElement.GetProperty(key).GetString(), text);
    }

    [Fact]
    public void FileOpenUsesFirstFlagAndRejectsMissingUnsupportedAndNetworkPaths()
    {
        using var dir = new TempFolder();
        var media = Path.Combine(dir.Path, "sample.MP4");
        File.WriteAllText(media, "test");
        Assert.Equal(("edit", media), DeeperFileOpen.Parse(new[] { "--edit", media, "--play", media }));
        Assert.Equal(("play", media), DeeperFileOpen.Parse(new[] { "--play", media }));
        Assert.Equal(("edit", media), DeeperFileOpen.Decode(DeeperFileOpen.Encode(new[] { "--edit", media })));
        Assert.Null(DeeperFileOpen.Decode("deeper:destroy\n" + media).Action);
        Assert.Null(DeeperFileOpen.Decode("game:race").Action);
        Assert.Null(DeeperFileOpen.Parse(new[] { "--PLAY", media }).Action);
        Assert.Null(DeeperFileOpen.Parse(new[] { "--edit" }).Action);
        Assert.Null(DeeperFileOpen.Parse(new[] { "--play", media + ".missing" }).Action);
        Assert.Null(DeeperFileOpen.Parse(new[] { "--play", @"\\server\sample.mp4" }).Action);
        var text = Path.Combine(dir.Path, "sample.txt");
        File.WriteAllText(text, "test");
        Assert.Null(DeeperFileOpen.Parse(new[] { "--edit", text }).Action);
    }

    private sealed class TempFolder : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("deeper-entry-").FullName;
        public void Dispose() => Directory.Delete(Path, true);
    }
}
