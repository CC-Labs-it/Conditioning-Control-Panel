using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace CCP.Core.Tests;

public sealed class OnlineOfficialNoteTests
{
    [Fact]
    public void AllTenLanguagesAlreadyHaveWordsOnlyHelpAndOfficialCopy()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !Directory.Exists(Path.Combine(root.FullName, "CCP.Core", "Localization"))) root = root.Parent;
        Assert.NotNull(root);
        var files = Directory.GetFiles(Path.Combine(root.FullName, "CCP.Core", "Localization", "Languages"), "*.json");
        Assert.Equal(10, files.Length);
        foreach (var file in files)
        {
            var json = JObject.Parse(File.ReadAllText(file));
            foreach (var key in new[] { "online_help_title", "online_help_share", "online_help_counts", "online_help_cache",
                "online_help_spent", "online_help_local", "online_official_stamp", "online_official_title",
                "online_official_body", "online_official_nice", "online_official_stamped_line",
                "online_official_plan_free", "online_official_plan_basic", "online_official_plan_prime" })
                Assert.False(string.IsNullOrWhiteSpace((string?)json[key]), Path.GetFileName(file) + ": " + key);
            foreach (var entry in json.Properties().Where(p => p.Name.StartsWith("online_", StringComparison.Ordinal)))
                Assert.False(Regex.IsMatch((string?)entry.Value ?? "", @"\b(?:MB|GB)\b"), entry.Name);
        }
    }

    [Theory]
    [InlineData("online", true, true, true, true)]
    [InlineData("mixed", true, true, true, true)]
    [InlineData("local", true, true, true, false)]
    [InlineData(null, true, true, true, false)]
    [InlineData("Online", true, true, true, false)]
    [InlineData("online", false, true, true, false)]
    [InlineData("online", true, false, true, false)]
    [InlineData("online", true, true, false, false)]
    public void CreditRequiresOnlineConsentSwitchAndSignedLicence(string? source, bool consent,
        bool enabled, bool official, bool expected)
        => Assert.Equal(expected, OnlineOfficialNoteRules.ShowCredit(source, consent, enabled, official));

    [Fact]
    public void PlanCopyMatchesWpf()
    {
        Assert.Equal("online_official_plan_free", OnlineOfficialNoteRules.PlanKey(ScrolllerPlan.Free));
        Assert.Equal("online_official_plan_basic", OnlineOfficialNoteRules.PlanKey(ScrolllerPlan.Basic));
        Assert.Equal("online_official_plan_prime", OnlineOfficialNoteRules.PlanKey(ScrolllerPlan.Prime));
    }
}
