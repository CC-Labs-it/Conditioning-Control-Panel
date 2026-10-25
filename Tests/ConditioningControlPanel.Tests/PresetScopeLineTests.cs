using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using ConditioningControlPanel.Models;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1427: nothing said which settings a preset saves. The presets page now carries one
/// line (loc key <c>sd_presets_scope</c>). These tests keep that line present, translated, and
/// TRUE: it promises that fonts and colours are not part of a preset, so a later change that puts
/// one into <see cref="Preset"/> has to reword the line.
/// </summary>
public class PresetScopeLineTests
{
    private const string Key = "sd_presets_scope";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    [Fact]
    public void PresetsPage_ShowsTheScopeLine()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Views", "Tabs", "PresetsTabView.xaml"));
        Assert.Contains("{loc:Str " + Key + "}", xaml);
    }

    [Fact]
    public void ScopeLine_IsInEveryLanguage_AndTranslated()
    {
        var dir = SourceRoots.LanguagesDirectory;
        var files = Directory.GetFiles(dir, "*.json");
        Assert.True(files.Length >= 9, "expected at least nine language files, found " + files.Length);

        string Read(string path)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            Assert.True(doc.RootElement.TryGetProperty(Key, out var v), Path.GetFileName(path) + " lacks " + Key);
            return v.GetString() ?? "";
        }

        var english = Read(Path.Combine(dir, "en.json"));
        foreach (var file in files)
        {
            var text = Read(file);
            Assert.False(string.IsNullOrWhiteSpace(text), Path.GetFileName(file));
            // House copy rules: no em or en dash, no exclamation mark.
            Assert.DoesNotContain('—', text);
            Assert.DoesNotContain('–', text);
            Assert.DoesNotContain('!', text);
            if (!Path.GetFileName(file).Equals("en.json", StringComparison.OrdinalIgnoreCase))
                Assert.NotEqual(english, text);
        }
    }

    [Fact]
    public void ScopeLine_IsTrue_APresetCarriesNoFontColourOrMediaSetting()
    {
        var names = typeof(Preset).GetProperties().Select(p => p.Name).ToList();

        // What the line says a preset leaves alone.
        foreach (var word in new[] { "Font", "Color", "Colour", "Folder", "Path", "MediaSource" })
            Assert.DoesNotContain(names, n => n.Contains(word, StringComparison.OrdinalIgnoreCase));

        // What the line says a preset saves: effect switches, dials, volumes, phrase picks.
        Assert.Contains("FlashEnabled", names);
        Assert.Contains("FlashFrequency", names);
        Assert.Contains("MasterVolume", names);
        Assert.Contains("SubliminalPool", names);
        Assert.Contains("LockCardPhrases", names);
    }
}
