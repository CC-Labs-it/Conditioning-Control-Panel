using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel;
using Newtonsoft.Json.Linq;
using Xunit;
namespace CCP.Core.Tests;
public class ModImageSlotRulesTests
{
    [Fact] public void ExtensionAliasesAndSizeBoundaryMatchWpf()
    {
        Assert.Null(ModImageSlotRules.DescribeExtensionMismatch("x.JPG", "x.jpeg"));
        Assert.Null(ModImageSlotRules.DescribeExtensionMismatch("preview", "x.jpg"));
        Assert.NotNull(ModImageSlotRules.DescribeExtensionMismatch("x.png", "x.jpg"));
        Assert.False(ModImageSlotRules.IsOversized(4L * 1024 * 1024));
        Assert.True(ModImageSlotRules.IsOversized(4L * 1024 * 1024 + 1));
    }
    [Fact] public void EveryLanguageDefinesAllArtStrings()
    {
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Localization", "Languages"), "*.json");
        Assert.Equal(10, files.Length);
        var keys = JObject.Parse(File.ReadAllText(files.Single(f => Path.GetFileName(f) == "en.json"))).Properties().Where(p => p.Name.StartsWith("mod_art_")).Select(p => p.Name).ToArray();
        Assert.Equal(49, keys.Length);
        foreach (var file in files) foreach (var key in keys) Assert.False(string.IsNullOrWhiteSpace((string?)JObject.Parse(File.ReadAllText(file))[key]), file + ": " + key);
    }
}
