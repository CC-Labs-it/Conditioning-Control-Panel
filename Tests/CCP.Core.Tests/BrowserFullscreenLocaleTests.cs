using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace CCP.Core.Tests;

public class BrowserFullscreenLocaleTests
{
    [Fact]
    public void FullscreenPlaceholderExistsInAllTenLanguages()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "CCP.Core", "Localization", "Languages"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var languages = Path.Combine(dir!.FullName, "CCP.Core", "Localization", "Languages");
        var files = Directory.GetFiles(languages, "*.json");
        Assert.Equal(10, files.Length);
        foreach (var file in files)
        {
            using var json = JsonDocument.Parse(File.ReadAllText(file));
            Assert.True(json.RootElement.TryGetProperty("label_browser_in_fullscreen", out var value), file);
            Assert.False(string.IsNullOrWhiteSpace(value.GetString()), file);
        }
    }
}
