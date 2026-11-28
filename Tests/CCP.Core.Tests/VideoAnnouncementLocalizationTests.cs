using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Core.Tests;

public sealed class VideoAnnouncementLocalizationTests
{
    [Fact]
    public void EveryLanguageContainsVideoAnnouncement()
    {
        foreach (var (code, _, _) in LocalizationManager.AvailableLanguages)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Localization", "Languages", code + ".json");
            var strings = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;
            Assert.True(strings.TryGetValue("avatar_video_announcement", out var value), code);
            Assert.False(string.IsNullOrWhiteSpace(value));
        }
    }
}
