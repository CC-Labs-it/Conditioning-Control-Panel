using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace CCP.Core.Tests;

public sealed class ChaosEStimLocalizationTests
{
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
    public void Every_language_has_valid_EStim_feed_templates(string language)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Localization", "Languages", language + ".json");
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var key in new[] { "chaos_estim_charged_conduct", "chaos_estim_charged_chain", "chaos_estim_arc_remaining" })
        {
            var template = json.RootElement.GetProperty(key).GetString()!;
            Assert.Contains("{0}", template);
            Assert.Contains("7", string.Format(template, 7));
        }
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("chaos_estim_spent").GetString()));
    }
}
