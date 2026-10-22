using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Every conversion row string (conv_*) is in all nine language files on main, in the
/// house voice: no em or en dashes, no exclamation marks.</summary>
public class ConversionCopyTests
{
    private static readonly string EmDash = ((char)0x2014).ToString();
    private static readonly string EnDash = ((char)0x2013).ToString();

    public static IEnumerable<object[]> ConvKeys()
    {
        var en = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(LangPath("en")))!;
        return en.Keys.Where(k => k.StartsWith("conv_", StringComparison.Ordinal)).Select(k => new object[] { k });
    }

    [Theory]
    [MemberData(nameof(ConvKeys))]
    public void ConversionKeys_AreInAllNineLanguages_WithoutDashesOrBangs(string key)
    {
        foreach (var lang in new[] { "en", "de", "es", "fr", "ja", "ko", "pt-BR", "ru", "zh-CN" })
        {
            var map = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(LangPath(lang)))!;
            Assert.True(map.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v), $"{lang} misses {key}");
            Assert.DoesNotContain(EmDash, v);
            Assert.DoesNotContain(EnDash, v);
            Assert.DoesNotContain("!", v);
        }
    }

    private static string LangPath(string lang)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Localization", "Languages")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "ConditioningControlPanel", "Localization", "Languages", lang + ".json");
    }
}
