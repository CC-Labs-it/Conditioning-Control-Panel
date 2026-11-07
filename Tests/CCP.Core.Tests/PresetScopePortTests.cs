using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Core.Tests;

public class PresetScopePortTests
{
    [Fact]
    public void ScopeCopyIsWiredTranslatedAndMatchesThePresetModel()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !Directory.Exists(Path.Combine(root.FullName, "CCP.Avalonia"))) root = root.Parent;
        Assert.NotNull(root);
        var xaml = XDocument.Load(Path.Combine(root.FullName, "CCP.Avalonia/Views/Tabs/PresetsTabView.axaml"));
        var line = Assert.Single(xaml.Descendants().Where(e => (string?)e.Attribute("Text") == "{loc:Str sd_presets_scope}"));
        Assert.Equal("TextBlock", line.Name.LocalName);
        Assert.Equal("Wrap", (string?)line.Attribute("TextWrapping"));
        var languages = Directory.GetFiles(Path.Combine(root.FullName, "CCP.Core/Localization/Languages"), "*.json");
        Assert.Equal(10, languages.Length);
        foreach (var file in languages)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var text = doc.RootElement.GetProperty("sd_presets_scope").GetString();
            Assert.False(string.IsNullOrWhiteSpace(text));
            foreach (var mark in new[] { "—", "–", "!" }) Assert.DoesNotContain(mark, text);
        }
        var names = typeof(Preset).GetProperties().Select(p => p.Name).ToArray();
        foreach (var word in new[] { "Font", "Color", "Colour", "Folder", "Path", "MediaSource" })
            Assert.DoesNotContain(names, n => n.Contains(word, StringComparison.OrdinalIgnoreCase));
        foreach (var name in new[] { "FlashEnabled", "FlashFrequency", "MasterVolume", "SubliminalPool", "LockCardPhrases" })
            Assert.Contains(name, names);
    }
}
