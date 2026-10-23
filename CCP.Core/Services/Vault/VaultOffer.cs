using System;
using System.Collections.Generic;
using System.Globalization;
using ConditioningControlPanel.Models;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Vault;

/// <summary>
/// The pure half of the vault gate card (Dialogs/VaultGateDialog): which feature a padlock
/// belongs to, what the tiers cost, the supporter-count line, and when the last-day card for an
/// invite week is owed. Prices come from the plan list (<see cref="PlanCatalog"/>) through <see cref="PriceFor"/>.
/// </summary>
public enum PriceCurrency { Usd, Eur }

/// <summary>One tier's price in one currency, in cents.</summary>
public sealed record TierPrice(PriceCurrency Currency, int MonthlyCents, int YearlyCents);

public static class VaultOffer
{
    /// <summary>
    /// One tier's price in one currency, from the plan list (<see cref="PlanCatalog"/>, server
    /// <c>/config/plans</c> with a compiled fallback). Dollars are the headline; euros are the
    /// secondary line (PayPal charges in euros). Yearly exists on Patreon only and is quoted in
    /// the headline currency; a euro yearly figure keeps the same discount.
    /// <para>Prices live in the plan list and nowhere else in the client.</para>
    /// </summary>
    public static TierPrice PriceFor(int tier, PriceCurrency currency, PlanSet? plans = null)
    {
        var plan = (plans ?? PlanCatalog.Current).For(tier);
        if (currency == PriceCurrency.Usd) return new TierPrice(currency, plan.MonthlyCents, plan.YearlyCents);
        var ratio = plan.MonthlyCents > 0 ? plan.YearlyCents / (decimal)plan.MonthlyCents : 12m;
        return new TierPrice(currency, plan.MonthlyEurCents,
            (int)Math.Round(plan.MonthlyEurCents * ratio, MidpointRounding.AwayFromZero));
    }

    /// <summary>The plan list's headline currency (C1 says USD).</summary>
    public static PriceCurrency HeadlineCurrency(PlanSet? plans = null) => CurrencyFor((plans ?? PlanCatalog.Current).Currency);

    /// <summary>"EUR" is euros, anything else dollars.</summary>
    public static PriceCurrency CurrencyFor(string? isoCurrencySymbol)
        => string.Equals(isoCurrencySymbol, "EUR", StringComparison.OrdinalIgnoreCase) ? PriceCurrency.Eur : PriceCurrency.Usd;

    /// <summary>"€6", "$7.50", "€0.20": whole amounts without decimals, anything else with two.</summary>
    public static string Money(int cents, PriceCurrency currency)
    {
        var symbol = currency == PriceCurrency.Eur ? "€" : "$";
        return cents % 100 == 0
            ? symbol + (cents / 100).ToString(CultureInfo.InvariantCulture)
            : symbol + (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
    }

    /// <summary>Monthly price over 30 days, rounded to the cent: "€0.20", "$0.42".</summary>
    public static string PerDay(TierPrice price)
        => Money((int)Math.Round(price.MonthlyCents / 30m, MidpointRounding.AwayFromZero), price.Currency);

    /// <summary>Yearly price over 12 months, rounded to the cent: "€5", "$10.42".</summary>
    public static string YearlyPerMonth(TierPrice price)
        => Money((int)Math.Round(price.YearlyCents / 12m, MidpointRounding.AwayFromZero), price.Currency);

    /// <summary>
    /// The feature behind a tab's padlock, from the tab view's type name. Every gated tab forwards
    /// its unlock button to <c>MainWindow.BtnGateUnlock_Click</c>, so this is how the card knows
    /// what was clicked. Null (a generic vault card) for anything it does not recognise.
    /// </summary>
    public static string? FeatureKeyForView(string? viewTypeName) => viewTypeName switch
    {
        "LockdownTabView" => "lockdown",
        "BambiTakeoverTabView" => "bambitakeover",
        "HapticsTabView" => "haptics",
        "AwarenessTabView" => "awareness",
        "RemoteControlTabView" => "remotecontrol",
        "SheListeningTabView" => "shelistening",
        "BlinkTrainerTabView" => "blinktrainer",
        _ => null,
    };

    /// <summary>
    /// Padlocks whose button must keep going to Settings · Account: Graded Intake's CTA also
    /// serves its "sign in" state and a tier-1 patron's spent weekly pass, neither of which a
    /// price card answers.
    /// </summary>
    public static bool KeepsAccountRoute(string? viewTypeName) => viewTypeName == "GradedIntakeTabView";

    /// <summary>The registry entry for a feature key, or null.</summary>
    public static ExclusiveFeature? Feature(string? key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        foreach (var f in ExclusiveFeature.All)
            if (string.Equals(f.Key, key, StringComparison.Ordinal)) return f;
        return null;
    }

    /// <summary>
    /// The supporter count as the card says it: rounded DOWN to the hundred and shown as "1,200",
    /// so the "+" in the copy is always true. Null below 100 or when unknown: the line is left
    /// out rather than shown with a small or invented number.
    /// </summary>
    public static string? SupporterFloor(int? count)
    {
        if (count is not int n || n < 100) return null;
        return (n / 100 * 100).ToString("N0", CultureInfo.InvariantCulture);
    }

    /// <summary><c>GET /v2/public/supporters</c> → <c>{ "count": 1234 }</c>. Null for anything else.</summary>
    public static int? ParseSupporterCount(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            var token = JObject.Parse(body)["count"];
            return token?.Type == JTokenType.Integer && token.Value<long>() is long v && v >= 0 && v < int.MaxValue ? (int)v : null;
        }
        catch (Exception ex) { Diag.Swallowed(ex, "supporters body not JSON"); return null; }
    }

    /// <summary>The last-day card shows inside this window before an invite week ends.</summary>
    public static readonly TimeSpan EndingWindow = TimeSpan.FromHours(36);

    /// <summary>
    /// The seen-flag key of the last-day card this invite week still owes, or null. Owed only to
    /// premium that comes from the week alone, only inside <see cref="EndingWindow"/>, and once
    /// per week (the key carries the week's end, so a later week is owed its own card).
    /// </summary>
    public static string? InviteEndingOwed(DateTime? grantUntilUtc, DateTime nowUtc, bool inviteWeekOnly, ICollection<string>? seen)
    {
        if (!inviteWeekOnly || grantUntilUtc is not DateTime end) return null;
        var left = end - nowUtc;
        if (left <= TimeSpan.Zero || left > EndingWindow) return null;
        var key = "invite-ending:" + end.ToUniversalTime().ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
        return seen != null && seen.Contains(key) ? null : key;
    }
}
