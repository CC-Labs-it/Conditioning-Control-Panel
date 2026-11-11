using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace CCP.Core.Tests;

public class ChaosChaperoneLocalizationTests
{
    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("ja")] [InlineData("ko")] [InlineData("zh-CN")]
    [InlineData("pt-BR")] [InlineData("fr")] [InlineData("it")] [InlineData("de")] [InlineData("ru")]
    public void Debut_strings_are_present_in_each_language(string language)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Localization", "Languages", language + ".json")));
        foreach (var key in new[] { "chaos_chaperone_title", "chaos_chaperone_subtitle", "chaos_chaperone_event" })
            Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty(key).GetString()));
    }
}
