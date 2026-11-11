using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Core.Tests;

public sealed class ChaosEStimLocalizationTests
{
    [Fact]
    public void Every_language_has_formattable_estim_events()
    {
        var loc = LocalizationManager.Instance;
        var previous = loc.CurrentLanguage;
        string[] keys = { "Chaos_EStim_Conduct", "Chaos_EStim_Chain", "Chaos_EStim_Arc", "Chaos_EStim_Spent" };
        try
        {
            foreach (var (code, _, _) in LocalizationManager.AvailableLanguages)
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Localization", "Languages", code + ".json");
                var strings = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;
                loc.SetLanguage(code);
                foreach (var key in keys)
                {
                    Assert.True(strings.TryGetValue(key, out var value), $"{code}: {key}");
                    Assert.False(string.IsNullOrWhiteSpace(value));
                    Assert.Equal(string.Format(value!, 7), loc.GetF(key, 7));
                    if (key != "Chaos_EStim_Spent") Assert.Contains("7", loc.GetF(key, 7));
                }
            }
        }
        finally { loc.SetLanguage(previous); }
    }
}
