using System;
using System.IO;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Conversion pipe fixes (2026-10-13): the DtRH capture switch is DEBUG only, Blink Trainer's
/// Start is a hard gate, and a spent Intake pass or a spent reroll leads to the priced card.
/// Source-level checks: these handlers need a live MainWindow.
/// </summary>
public class ConversionPipeTests
{
    private static readonly string[] Languages = { "en", "de", "es", "fr", "ja", "ko", "pt-BR", "ru", "zh-CN" };

    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return Path.Combine(dir!.FullName, "ConditioningControlPanel");
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(AppDir(), Path.Combine(parts)));

    private static string Body(string src, string signature, int length)
    {
        var at = src.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(at >= 0, "missing " + signature);
        return src.Substring(at, Math.Min(length, src.Length - at));
    }

    [Fact]
    public void TheDtrhCaptureSwitchIsDebugOnly()
    {
        var src = Read("App.xaml.cs");
        var use = src.IndexOf("e.Args.Contains(\"--dtrh-m2test\")", StringComparison.Ordinal);
        Assert.True(use > 0);
        var ifDebug = src.LastIndexOf("#if DEBUG", use, StringComparison.Ordinal);
        var endif = src.IndexOf("#endif", use, StringComparison.Ordinal);
        Assert.True(ifDebug > 0 && endif > use, "--dtrh-m2test must sit inside #if DEBUG");
        Assert.Equal(-1, src.IndexOf("#endif", ifDebug, use - ifDebug, StringComparison.Ordinal));
        Assert.Equal(-1, src.IndexOf("#else", ifDebug, endif - ifDebug, StringComparison.Ordinal));
        // The gated --dtrh door is still compiled in every build.
        Assert.True(src.IndexOf("e.Args.Contains(\"--dtrh\")", endif, StringComparison.Ordinal) > endif);
    }

    [Fact]
    public void BlinkTrainerStartIsAHardGate()
    {
        var body = Body(Read("MainWindow", "MainWindow.BlinkTrainer.cs"), "BtnBlinkTrainerStartSession_Click", 1200);
        var gate = body.IndexOf("TierGate.DemandPremium(", StringComparison.Ordinal);
        var start = body.IndexOf("App.BlinkTrainer.Start()", StringComparison.Ordinal);
        Assert.True(gate > 0, "the Start handler must call TierGate.DemandPremium");
        Assert.True(start < 0 || gate < start);
    }

    [Fact]
    public void ASpentIntakePassOpensThePricedCard()
    {
        var body = Body(Read("MainWindow", "MainWindow.Lab.cs"), "intake_gate_spent_cta", 200);
        Assert.Contains("OpenPricedCard(\"gradedintake\"", body);
        Assert.DoesNotContain("ShowAppInfoPopup", body);
    }

    [Fact]
    public void ASpentRerollOffersBasic()
    {
        var src = Read("MainWindow", "MainWindow.QuestsTab.cs");
        var tell = Body(src, "private void TellRerollsSpent", 1200);
        Assert.Contains("quest_reroll_see_basic", tell);
        Assert.Contains("OpenPricedCard(\"quest_reroll\", 1)", tell);
        var open = Body(src, "internal void OpenPricedCard", 300);
        Assert.Contains("TierGate.ReconnectIsTheAnswer()", open);
        Assert.Contains("ShowVaultGate(featureKey, tier)", open);
    }

    [Fact]
    public void TheRerollCopyNamesBasicInEveryLanguage()
    {
        foreach (var lang in Languages)
        {
            var json = JObject.Parse(File.ReadAllText(Path.Combine(SourceRoots.LanguagesDirectory, lang + ".json")));
            foreach (var key in new[] { "quest_reroll_see_basic", "quest_reroll_daily_none_free", "quest_reroll_weekly_none_free" })
            {
                var v = json.Value<string>(key);
                Assert.False(string.IsNullOrWhiteSpace(v), $"{lang}.json is missing {key}");
                Assert.Contains("Basic", v);
                Assert.DoesNotContain("Patreon", v);
            }
        }
    }

    [Fact]
    public void TheDailyFeatureCommentNoLongerSaysTheRouteIsMissing()
        => Assert.DoesNotContain("does not exist yet", SourceRoots.ReadProductFile("Services", "DailyFreeService.cs"));
}
