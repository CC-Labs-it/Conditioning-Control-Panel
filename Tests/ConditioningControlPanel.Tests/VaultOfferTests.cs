using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Services.Vault;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The vault gate card's decisions: which feature a padlock belongs to, what the tiers cost, the
/// supporter line (never a made-up number), and when an invite week's last-day card is owed.
/// </summary>
public class VaultOfferTests
{
    private static readonly DateTime Now = new(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);
    private static readonly string[] Languages = { "en", "de", "es", "fr", "it", "ja", "ko", "pt-BR", "ru", "zh-CN" };

    // The compiled plan list (contract C1): USD headline, EUR secondary, yearly 20% off.
    [Theory]
    [InlineData(1, PriceCurrency.Eur, "€6", "€0.20", "€57.60", "€4.80")]
    [InlineData(2, PriceCurrency.Eur, "€10", "€0.33", "€96", "€8")]
    [InlineData(1, PriceCurrency.Usd, "$7.50", "$0.25", "$72", "$6")]
    [InlineData(2, PriceCurrency.Usd, "$12.50", "$0.42", "$120", "$10")]
    [InlineData(0, PriceCurrency.Usd, "$7.50", "$0.25", "$72", "$6")]
    public void ThePriceListFallback(int tier, PriceCurrency currency, string monthly, string perDay, string yearly, string yearlyPerMonth)
    {
        var price = VaultOffer.PriceFor(tier, currency, PlanCatalog.Fallback);
        Assert.Equal(monthly, VaultOffer.Money(price.MonthlyCents, currency));
        Assert.Equal(perDay, VaultOffer.PerDay(price));
        Assert.Equal(yearly, VaultOffer.Money(price.YearlyCents, currency));
        Assert.Equal(yearlyPerMonth, VaultOffer.YearlyPerMonth(price));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void YearlyIsTwentyPercentOff(int tier)
        => Assert.Equal(20, PlanCatalog.YearlyOffPercent(PlanCatalog.Fallback.For(tier)));

    [Fact]
    public void TheHeadlineIsDollars() => Assert.Equal(PriceCurrency.Usd, VaultOffer.HeadlineCurrency(PlanCatalog.Fallback));

    [Theory]
    [InlineData("EUR", PriceCurrency.Eur)]
    [InlineData("eur", PriceCurrency.Eur)]
    [InlineData("USD", PriceCurrency.Usd)]
    [InlineData("GBP", PriceCurrency.Usd)]
    [InlineData(null, PriceCurrency.Usd)]
    public void EurosOnlyWhenThePlanListSaysEuros(string? iso, PriceCurrency expected)
        => Assert.Equal(expected, VaultOffer.CurrencyFor(iso));

    [Fact]
    public void TheCardNeverOpensTheBareProfile()
    {
        var src = File.ReadAllText(Path.Combine(AppDir(), "Dialogs", "VaultGateDialog.cs"));
        Assert.DoesNotContain("patreon.com/CodeBambi\"", src);
        Assert.Contains("plan.PatreonUrl", src);
        Assert.Contains("PlanCatalog.WebUrlFor(", src);
    }

    [Theory]
    [InlineData("LockdownTabView", "lockdown")]
    [InlineData("BambiTakeoverTabView", "bambitakeover")]
    [InlineData("HapticsTabView", "haptics")]
    [InlineData("AwarenessTabView", "awareness")]
    [InlineData("RemoteControlTabView", "remotecontrol")]
    [InlineData("SheListeningTabView", "shelistening")]
    [InlineData("BlinkTrainerTabView", "blinktrainer")]
    public void EveryGatedViewNamesARealFeature(string view, string key)
    {
        Assert.Equal(key, VaultOffer.FeatureKeyForView(view));
        Assert.NotNull(VaultOffer.Feature(key));
    }

    [Theory]
    [InlineData("GradedIntakeTabView", true)]
    [InlineData("LockdownTabView", false)]
    [InlineData(null, false)]
    public void GradedIntakeKeepsTheAccountRoute(string? view, bool expected)
        => Assert.Equal(expected, VaultOffer.KeepsAccountRoute(view));

    [Theory]
    [InlineData(null)]
    [InlineData("SettingsTabView")]
    public void AnUnknownViewGetsTheGenericCard(string? view) => Assert.Null(VaultOffer.FeatureKeyForView(view));

    [Theory]
    [InlineData(null, null)]
    [InlineData(0, null)]
    [InlineData(99, null)]
    [InlineData(100, "100")]
    [InlineData(1234, "1,200")]
    [InlineData(1299, "1,200")]
    [InlineData(25080, "25,000")]
    public void SupporterFloorRoundsDownAndHidesSmallNumbers(int? count, string? expected)
        => Assert.Equal(expected, VaultOffer.SupporterFloor(count));

    [Theory]
    [InlineData("{\"count\":1234}", 1234)]
    [InlineData("{\"count\":\"1234\"}", null)]
    [InlineData("{\"count\":-1}", null)]
    [InlineData("{}", null)]
    [InlineData("<html>", null)]
    [InlineData("", null)]
    public void ParseSupporterCount(string body, int? expected)
        => Assert.Equal(expected, VaultOffer.ParseSupporterCount(body));

    [Fact]
    public void EndingIsOwedInsideTheWindowOnce()
    {
        var end = Now.AddHours(20);
        var key = VaultOffer.InviteEndingOwed(end, Now, inviteWeekOnly: true, new List<string>());
        Assert.Equal("invite-ending:202610030800", key);
        Assert.Null(VaultOffer.InviteEndingOwed(end, Now, true, new List<string> { key! }));
    }

    [Fact]
    public void EndingIsNotOwedTooEarlyAfterTheEndOrToAPayingAccount()
    {
        Assert.Null(VaultOffer.InviteEndingOwed(Now.AddDays(3), Now, true, null));
        Assert.Null(VaultOffer.InviteEndingOwed(Now.AddMinutes(-1), Now, true, null));
        Assert.Null(VaultOffer.InviteEndingOwed(Now.AddHours(5), Now, inviteWeekOnly: false, null));
        Assert.Null(VaultOffer.InviteEndingOwed(null, Now, true, null));
    }

    [Fact]
    public void ALaterWeekIsOwedItsOwnCard()
    {
        var first = VaultOffer.InviteEndingOwed(Now.AddHours(10), Now, true, null);
        var second = VaultOffer.InviteEndingOwed(Now.AddDays(40).AddHours(10), Now.AddDays(40), true, new List<string> { first! });
        Assert.NotNull(second);
        Assert.NotEqual(first, second);
    }

    // ---------------------------------------------------------------- words

    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return Path.Combine(dir!.FullName, "ConditioningControlPanel");
    }

    private static string Placeholders(string s)
        => string.Join(",", Regex.Matches(s, @"\{\d\}").Select(m => m.Value).OrderBy(v => v));

    [Fact]
    public void EveryKeyTheCardsReadIsInEveryLanguageWithTheSamePlaceholders()
    {
        var src = File.ReadAllText(Path.Combine(AppDir(), "Dialogs", "VaultGateDialog.cs"))
                  + File.ReadAllText(Path.Combine(AppDir(), "MainWindow", "MainWindow.Patreon.cs"));
        var keys = Regex.Matches(src, "\"(vaultgate_[a-z_0-9]+)\"").Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.Contains("vaultgate_open_vault", keys);
        Assert.Contains("vaultgate_ending_inbox", keys);

        var en = JObject.Parse(File.ReadAllText(Path.Combine(AppDir(), "Localization", "Languages", "en.json")));
        foreach (var lang in Languages)
        {
            var json = JObject.Parse(File.ReadAllText(Path.Combine(AppDir(), "Localization", "Languages", lang + ".json")));
            foreach (var key in keys)
            {
                var value = json.Value<string>(key);
                Assert.False(string.IsNullOrWhiteSpace(value), $"{lang}.json is missing {key}");
                Assert.Equal(Placeholders(en.Value<string>(key)!), Placeholders(value!));
            }
        }
    }

    [Fact]
    public void EveryPadlockButtonGoesThroughTheGateCard()
    {
        var patreon = File.ReadAllText(Path.Combine(AppDir(), "MainWindow", "MainWindow.Patreon.cs"));
        var at = patreon.IndexOf("internal void BtnGateUnlock_Click", StringComparison.Ordinal);
        Assert.True(at > 0);
        var body = patreon.Substring(at, 900);
        Assert.Contains("ShowVaultGate(", body);
        Assert.Contains("TierGate.ReconnectIsTheAnswer()", body);
        Assert.Contains("StartPatreonReconnectFromGate()", body);
    }
}
