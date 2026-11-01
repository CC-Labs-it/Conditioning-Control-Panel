using System;
using System.IO;
using System.Text.Json;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Core.Tests;

public sealed class RemoteMediaOfferLocalizationTests
{
    [Fact]
    public void OfferCopyExistsInEveryLanguage()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !Directory.Exists(Path.Combine(root.FullName, "CCP.Core", "Localization", "Languages")))
            root = root.Parent;
        Assert.NotNull(root);
        foreach (var (code, _, _) in LocalizationManager.AvailableLanguages)
        {
            var path = Path.Combine(root!.FullName, "CCP.Core", "Localization", "Languages", code + ".json");
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var key in new[] { "remote_media_offer_title", "remote_media_offer_summary" })
            {
                Assert.True(json.RootElement.TryGetProperty(key, out var value), $"{code} is missing {key}");
                var text = value.GetString();
                Assert.False(string.IsNullOrWhiteSpace(text), $"{code} has empty {key}");
                Assert.DoesNotContain('!', text!);
                Assert.DoesNotContain('–', text!);
                Assert.DoesNotContain('—', text!);
            }
        }
        Assert.Equal(10, LocalizationManager.AvailableLanguages.Length);
    }
}
