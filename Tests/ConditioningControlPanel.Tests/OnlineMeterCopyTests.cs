using System;
using System.IO;
using ConditioningControlPanel.Services.Fyp.Online;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// "Online media today" copy (owner mockup, 2026-10-19): every key the meter, the pill, the spent
/// notice and the official note read is in all ten language files, and every format the code
/// fills keeps its number slot.
/// </summary>
public class OnlineMeterCopyTests
{
    private static readonly string[] Keys =
    {
        "online_meter_label", "online_meter_pct", "online_see_plans", "online_meter_spent", "online_meter_hours",
        "online_meter_minutes", "online_pill", "online_pill_done", "online_hover_title", "online_hover_body",
        "online_toast_spent",
        "online_official_stamp", "online_official_title", "online_official_body", "online_official_nice", "online_official_stamped_line",
    };

    private static readonly string[] Formats =
        { "online_meter_pct", "online_pill", "online_meter_spent", "online_meter_hours", "online_meter_minutes" };

    [Fact]
    public void EveryCopyKeyIsInEveryLanguageFile()
    {
        var files = Directory.GetFiles(LanguageDir(), "*.json");
        Assert.Equal(10, files.Length);
        foreach (var f in files)
        {
            var o = JObject.Parse(File.ReadAllText(f));
            foreach (var plan in new[] { ScrolllerPlan.Free, ScrolllerPlan.Basic, ScrolllerPlan.Prime })
            {
                AssertKey(o, f, OnlineMeterRules.UnderKey(plan));
                if (OnlineMeterRules.UpsellKey(plan) is string upsell) AssertKey(o, f, upsell);
                AssertKey(o, f, OnlineMeterRules.OfficialPlanKey(plan));
            }
            foreach (var k in Keys) AssertKey(o, f, k);
            foreach (var k in Formats) Assert.Contains("{0}", (string)o[k]!);
        }
    }

    [Fact]
    public void EnglishCopyIsTheMockups()
    {
        var o = JObject.Parse(File.ReadAllText(Path.Combine(LanguageDir(), "en.json")));
        Assert.Equal("Online media today", (string?)o["online_meter_label"]);
        Assert.Equal("Online {0}%", (string?)o["online_pill"]);
        Assert.Equal("Online done for today", (string?)o["online_pill_done"]);
        Assert.Equal("See plans", (string?)o["online_see_plans"]);
        // 300 MB free vs 4096 Basic and 7987 Prime (owner, 2026-10-19)
        Assert.Equal("Free plan. Basic gets about 14 times more a day, Prime about 27.", (string?)o["online_meter_upsell_free"]);
        Assert.Equal("Basic plan. Prime gets about twice as much a day.", (string?)o["online_meter_upsell_basic"]);
        Assert.Equal("Online media is official", (string?)o["online_official_title"]);
        // House voice: no em or en dashes, no exclamation marks, never a size or a count.
        foreach (var p in o.Properties())
        {
            if (!p.Name.StartsWith("online_", StringComparison.Ordinal)) continue;
            var v = (string)p.Value!;
            Assert.DoesNotContain("—", v);
            Assert.DoesNotContain("–", v);
            Assert.DoesNotContain("!", v);
            Assert.DoesNotContain("MB", v);
        }
    }

    private static void AssertKey(JObject o, string file, string key)
        => Assert.False(string.IsNullOrWhiteSpace((string?)o[key]), $"{Path.GetFileName(file)} has no {key}");

    internal static string LanguageDir()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null)
        {
            var p = Path.Combine(d.FullName, "ConditioningControlPanel", "Localization", "Languages");
            if (Directory.Exists(p)) return p;
            d = d.Parent;
        }
        throw new InvalidOperationException("language files not found");
    }
}
