using System;
using System.IO;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

public sealed class PossessionRetitleLocalizationTests
{
    [Fact]
    public void EveryLanguageSuppliesAllRetitleLines()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Localization", "Languages");
        var files = Directory.GetFiles(directory, "*.json");
        Assert.Equal(10, files.Length);
        var keys = new[] { "possession_retitle_here", "possession_retitle_exit", "possession_retitle_reading",
            "possession_retitle_timer", "possession_retitle_ends" };
        foreach (var file in files)
        {
            var translations = JObject.Parse(File.ReadAllText(file));
            foreach (var key in keys)
                Assert.True(translations.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value.Value<string>()),
                    $"{Path.GetFileName(file)} missing {key}");
        }
    }
}
