using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Core.Tests;

public sealed class FirstRaceCopyTests
{
    [Fact]
    public void AllLanguagesShipEveryCardStringWithSafeCopyAndProgressPlaceholder()
    {
        string[] keys = { "intro", "download", "cancel", "slower", "browser", "browser_warning", "progress",
            "unavailable", "network", "checksum", "storage", "unsupported", "failed", "start_failed", "browser_failed" };
        Assert.Equal(10, LocalizationManager.AvailableLanguages.Length);
        foreach (var (code, _, _) in LocalizationManager.AvailableLanguages)
        {
            var file = Path.Combine(AppContext.BaseDirectory, "Localization", "Languages", code + ".json");
            var strings = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file))!;
            foreach (var key in keys)
            {
                Assert.True(strings.TryGetValue("rt_engine_" + key, out var value), code + ": " + key);
                Assert.False(string.IsNullOrWhiteSpace(value));
                Assert.DoesNotContain('!', value!);
                Assert.DoesNotContain('—', value!);
                Assert.DoesNotContain('–', value!);
            }
            Assert.Contains("{0}", strings["rt_engine_progress"]);
            Assert.Contains("42", string.Format(strings["rt_engine_progress"], 42));
        }
    }
}
