using System;
using System.IO;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

public class ChaosBodyBuzzLocalizationTests
{
    [Fact]
    public void BodyBuzzFeedHasAnEntryInEachLanguage()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Localization", "Languages");
        var files = Directory.GetFiles(directory, "*.json");
        Assert.Equal(10, files.Length);
        foreach (var file in files)
        {
            var strings = JObject.Parse(File.ReadAllText(file));
            Assert.True(strings.TryGetValue("chaos_body_buzz_spreads", out var value)
                && !string.IsNullOrWhiteSpace(value.Value<string>()), Path.GetFileName(file));
        }
    }
}
