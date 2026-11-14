using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

public sealed class PersonalityStudioLocalizationTests
{
    // WPF Services/Companion/PersonalityStudio.cs:89-90 requires Yes/No for Delete.
    // Check raw dictionaries so the English fallback cannot conceal a missing translation.
    [Fact]
    public void EveryStudioLabelIncludingDeleteChoicesExistsInAllTenLanguages()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Localization", "Languages");
        var files = Directory.GetFiles(directory, "*.json");
        Assert.Equal(10, files.Length);
        var english = JObject.Parse(File.ReadAllText(Path.Combine(directory, "en.json")));
        var keys = english.Properties().Select(p => p.Name).Where(k => k.StartsWith("companion_studio_", StringComparison.Ordinal))
            .Concat(new[] { "btn_yes", "btn_no", "setting_screen_awareness_reactions" }).ToArray();
        foreach (var file in files)
        {
            var labels = JObject.Parse(File.ReadAllText(file));
            foreach (var key in keys)
                Assert.True(labels.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value.Value<string>()),
                    $"{Path.GetFileName(file)} is missing {key}");
            if (Path.GetFileName(file) == "it.json")
                Assert.Equal(("Sì", "No"), (labels.Value<string>("btn_yes"), labels.Value<string>("btn_no")));
        }
    }
}
