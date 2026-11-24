using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Core.Tests;

public sealed class DeeperPinLocalizationTests
{
    [Fact]
    public void AllLanguagesContainFormattablePinTooltips()
    {
        string[] keys = { "deeper_editor_pin_past_end_fmt", "deeper_editor_pin_invalid_time_fmt",
            "deeper_editor_pin_region_gone_fmt", "deeper_editor_pin_anywhere_fmt", "deeper_editor_pin_time_fmt" };
        foreach (var (code, _, _) in LocalizationManager.AvailableLanguages)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Localization", "Languages", code + ".json");
            var strings = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;
            foreach (var key in keys)
            {
                Assert.True(strings.TryGetValue(key, out var format), $"{code}: {key}");
                var value = string.Format(format!, 37, "detail");
                Assert.Contains("37", value);
                Assert.Contains("detail", value);
            }
        }
    }
}
