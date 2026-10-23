using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Vault;

/// <summary>One paid plan as <c>GET /config/plans</c> describes it. Money in cents.</summary>
public sealed record PlanInfo(
    string Key, string Name, int Tier,
    int MonthlyCents, int MonthlyEurCents, int YearlyCents,
    string PatreonUrl, string SubstarUrl, string WebUrl);

/// <summary>
/// The price list: both plans, the headline currency, the live first-month sale (if any) and
/// whether it came from the server or the compiled fallback.
/// </summary>
public sealed record PlanSet(string Currency, PlanInfo Basic, PlanInfo Prime, VaultSaleInfo? Sale, bool FromServer)
{
    /// <summary>Tier 2 and up is Prime, anything lower Basic.</summary>
    public PlanInfo For(int tier) => tier >= 2 ? Prime : Basic;

    /// <summary>"basic" or "prime" (any case); null for anything else.</summary>
    public PlanInfo? ForKey(string? key) => key?.Trim().ToLowerInvariant() switch
    {
        "basic" => Basic,
        "prime" => Prime,
        _ => null,
    };
}

/// <summary>
/// Reads the plan list (<c>GET /config/plans</c>, contract C1) through the proxy, caches it for ten
/// minutes and falls back to a compiled copy with the same values, so every "Get Basic / Get Prime"
/// button and price works offline. Every checkout button opens the plan's <see cref="PlanInfo.PatreonUrl"/>
/// (Patreon's checkout preselects the tier); the small card / PayPal link opens
/// <see cref="WebUrlFor"/> (the site's checkout, tagged <c>from=panel</c>).
/// <para>Parsing fails closed: one odd field and the whole server answer is ignored for the
/// fallback. Links must be https on a known host, because the app hands them to the browser.</para>
/// </summary>
public static class PlanCatalog
{
    public const string Path = "/config/plans";
    public static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);

    /// <summary>The compiled copy. Must stay equal to contract C1's defaults.</summary>
    public static readonly PlanSet Fallback = new(
        "USD",
        new PlanInfo("basic", "Basic", 1, 750, 600, 7200,
            "https://www.patreon.com/checkout/CodeBambi?rid=27604786",
            "https://subscribestar.adult/cc-labs",
            "https://app.cclabs.app/subscribe?plan=basic"),
        new PlanInfo("prime", "Prime", 2, 1250, 1000, 12000,
            "https://www.patreon.com/checkout/CodeBambi?rid=27681505",
            "https://subscribestar.adult/cc-labs",
            "https://app.cclabs.app/subscribe?plan=prime"),
        null, false);

    private static readonly string[] AllowedHosts =
        { "www.patreon.com", "patreon.com", "subscribestar.adult", "www.subscribestar.adult", "app.cclabs.app", "cclabs.app" };

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static PlanSet _current = Fallback;
    private static DateTime _readUtc = DateTime.MinValue;

    /// <summary>Test seam: replaces the network read. Returns the body, or null for a failed read.</summary>
    internal static Func<string, Task<string?>>? FetchOverride;

    /// <summary>The last list read this run, or the fallback. Never null, never waits.</summary>
    public static PlanSet Current => _current;

    /// <summary>
    /// The list, from the cache when it is younger than <see cref="CacheFor"/>, else from the
    /// server. A failed read keeps whatever was there (the fallback on a cold start) and is not
    /// retried for the same ten minutes, so a dead route costs one request per window.
    /// </summary>
    public static async Task<PlanSet> GetAsync(string? baseUrl = null)
    {
        if (DateTime.UtcNow - _readUtc < CacheFor) return _current;
        if (CoreSettings.Service?.Current?.OfflineMode == true) return _current;   // Offline Mode: the built-in list
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (DateTime.UtcNow - _readUtc < CacheFor) return _current;
            _readUtc = DateTime.UtcNow;
            var root = baseUrl ?? BackRoom.BackRoomApi.BaseUrl;
            if (string.IsNullOrWhiteSpace(root)) return _current;
            string? body = null;
            try
            {
                if (FetchOverride != null) body = await FetchOverride(root.TrimEnd('/') + Path).ConfigureAwait(false);
                else
                {
                    using var res = await Http.GetAsync(root.TrimEnd('/') + Path).ConfigureAwait(false);
                    if (res.IsSuccessStatusCode) body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex) { Diag.Swallowed(ex, "plans read failed"); }
            if (Parse(body) is PlanSet fresh) _current = fresh;
            return _current;
        }
        finally { Gate.Release(); }
    }

    /// <summary>Forget the cache (tests).</summary>
    internal static void Reset()
    {
        _current = Fallback;
        _readUtc = DateTime.MinValue;
        FetchOverride = null;
    }

    /// <summary>A C1 body as a <see cref="PlanSet"/>, or null for anything odd.</summary>
    public static PlanSet? Parse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var reader = new JsonTextReader(new StringReader(body)) { DateParseHandling = DateParseHandling.None };
            if (JToken.ReadFrom(reader) is not JObject o) return null;
            var currency = (o["currency"]?.Type == JTokenType.String ? o.Value<string>("currency") : null)?.Trim().ToUpperInvariant();
            if (currency != "USD" && currency != "EUR") return null;
            var basic = ParsePlan(o["basic"] as JObject, "basic", 1);
            var prime = ParsePlan(o["prime"] as JObject, "prime", 2);
            if (basic == null || prime == null) return null;
            VaultSaleInfo? sale = o["sale"] is JObject s ? VaultSale.Parse(s.ToString(Formatting.None)) : null;
            return new PlanSet(currency, basic, prime, sale, true);
        }
        catch (Exception ex) { Diag.Swallowed(ex, "plans body not JSON"); return null; }
    }

    private static PlanInfo? ParsePlan(JObject? p, string key, int tier)
    {
        if (p == null) return null;
        var monthly = Cents(p["monthly"]);
        var eur = Cents(p["monthly_eur"]);
        var yearly = Cents(p["yearly"]);
        if (monthly is not int m || eur is not int e || yearly is not int y) return null;
        var patreon = SafeUrl(p["patreon_url"]);
        var substar = SafeUrl(p["substar_url"]);
        var web = SafeUrl(p["web_url"]);
        if (patreon == null || substar == null || web == null) return null;
        var name = p["name"]?.Type == JTokenType.String ? p.Value<string>("name")!.Trim() : "";
        if (name.Length == 0 || name.Length > 24) name = tier >= 2 ? "Prime" : "Basic";
        return new PlanInfo(key, name, tier, m, e, y, patreon, substar, web);
    }

    /// <summary>"7.50" (a string, as C1 sends it) or a bare number, in cents; 1 cent to 1000.00.</summary>
    internal static int? Cents(JToken? t)
    {
        if (t == null) return null;
        decimal value;
        if (t.Type == JTokenType.String)
        {
            if (!decimal.TryParse(t.Value<string>(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)) return null;
        }
        else if (t.Type is JTokenType.Integer or JTokenType.Float) value = t.Value<decimal>();
        else return null;
        var cents = decimal.Round(value * 100m, 0, MidpointRounding.AwayFromZero);
        return cents >= 1 && cents <= 100_000 ? (int)cents : null;
    }

    /// <summary>An https link on a known host, or null.</summary>
    internal static string? SafeUrl(JToken? t)
    {
        if (t?.Type != JTokenType.String) return null;
        var raw = t.Value<string>()?.Trim();
        if (string.IsNullOrEmpty(raw) || raw.Length > 300) return null;
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps) return null;
        if (!string.IsNullOrEmpty(u.UserInfo) || !u.IsDefaultPort) return null;
        foreach (var host in AllowedHosts)
            if (string.Equals(u.Host, host, StringComparison.OrdinalIgnoreCase)) return u.AbsoluteUri;
        return null;
    }

    /// <summary>The site checkout for a plan, tagged with the surface: <c>...&amp;from=panel</c>.</summary>
    public static string WebUrlFor(PlanInfo plan, string surface = "panel")
    {
        var sep = plan.WebUrl.Contains('?') ? "&" : "?";
        return plan.WebUrl + sep + "from=" + Uri.EscapeDataString(surface);
    }

    /// <summary>
    /// The plan list as pages read it (the <c>plans</c> field of an init payload, contract C10):
    /// the same shape and string amounts as C1, with the web links already tagged
    /// <c>from=panel</c>. Sale stays null here; the card reads its own.
    /// </summary>
    public static JObject ToWire(PlanSet set) => new()
    {
        ["currency"] = set.Currency,
        ["basic"] = PlanWire(set.Basic),
        ["prime"] = PlanWire(set.Prime),
        ["yearly_note"] = YearlyOffPercent(set.Basic) + "% off",
        ["sale"] = null,
    };

    private static JObject PlanWire(PlanInfo p) => new()
    {
        ["name"] = p.Name,
        ["tier"] = p.Tier,
        ["monthly"] = Amount(p.MonthlyCents),
        ["monthly_eur"] = Amount(p.MonthlyEurCents),
        ["yearly"] = Amount(p.YearlyCents),
        ["patreon_url"] = p.PatreonUrl,
        ["substar_url"] = p.SubstarUrl,
        ["web_url"] = WebUrlFor(p),
    };

    private static string Amount(int cents) => (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>What yearly saves against twelve months, as a whole percent (C1: 20).</summary>
    public static int YearlyOffPercent(PlanInfo p)
    {
        var twelve = p.MonthlyCents * 12m;
        if (twelve <= 0 || p.YearlyCents >= twelve) return 0;
        return (int)Math.Round((1m - p.YearlyCents / twelve) * 100m, MidpointRounding.AwayFromZero);
    }
}
