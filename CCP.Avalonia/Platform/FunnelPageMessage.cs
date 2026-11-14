using System;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.Vault;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF FunnelPageMessage and FunnelBeacon for a game page's end card.</summary>
internal static class FunnelPageMessage
{
    private static readonly Regex Field = new("^[a-z0-9_]{1,32}$", RegexOptions.CultureInvariant);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private static readonly object Gate = new();
    private static DateTime _lastOpenUtc = DateTime.MinValue;

    internal static Func<DateTime> UtcNow = () => DateTime.UtcNow;
    internal static Func<string, bool> OpenCheckout = ExternalOpener.Open;
    internal static Func<string, string, Task>? SendOverride;
    internal static string? BaseUrlOverride;

    internal static void Handle(JObject message, bool mayOpen)
    {
        try
        {
            if (Str(message, "type") != "funnel") return;
            var evt = Str(message, "event");
            if (evt is not ("gate_shown" or "cta_clicked" or "checkout_started" or "ref")) return;
            var a = Clean(Str(message, "a"));
            var b = Clean(Str(message, "b"));
            string? url = null;
            if (evt == "cta_clicked" && PlanCatalog.Current.ForKey(Str(message, "plan")) is { } plan)
            {
                if (b is not ("web" or "substar")) b = "patreon";
                url = b switch
                {
                    "web" => PlanCatalog.WebUrlFor(plan, "panel"),
                    "substar" => plan.SubstarUrl,
                    _ => plan.PatreonUrl,
                };
            }
            try { Post(evt, a, b); }
            catch (Exception ex) { Log.Debug("[Funnel] beacon failed: {Error}", ex.Message); }
            if (url == null || !mayOpen || !TakeOpenSlot(UtcNow())) return;
            CheckoutWatch.NoteOpened(url, UtcNow());   // WPF FunnelPageMessage.cs:72
            OpenCheckout(url);
        }
        catch (Exception ex) { Log.Debug("[Funnel] page message failed: {Error}", ex.Message); }
    }

    /// <summary>The account re-read a return from checkout owes; tests swap it.</summary>
    internal static Func<Task> RefreshAccount = RefreshAccountAsync;

    /// <summary>WPF EntitlementTierSync.OnAppActivatedAfterCheckout: a window came to the front within the watch.</summary>
    internal static void OnAppActivated()
    {
        try
        {
            if (CoreSettings.Current.OfflineMode || !CheckoutWatch.TakeCheck(UtcNow())) return;
            Log.Debug("[Entitlement] back from a checkout: re-reading the tier");
            _ = RefreshAccount();
        }
        catch (Exception ex) { Log.Debug("[Entitlement] checkout hook failed: {Error}", ex.Message); }
    }

    private static async Task RefreshAccountAsync()
    {
        try
        {
            if (AccountSeed.Patreon is { IsAuthenticated: true } patreon) await patreon.ValidateSubscriptionAsync(forceRefresh: true);
            await AccountSeed.LoadProfileAsync();   // offers the server tier rise (EntitlementTierRule.ApplyRise)
            if (CoreAccount.HasPremiumAccess) CheckoutWatch.Disarm();   // WPF EntitlementTierSync.cs:90
        }
        catch (Exception ex) { Log.Debug("[Entitlement] checkout refresh failed: {Error}", ex.Message); }
    }

    private static bool TakeOpenSlot(DateTime now)
    {
        lock (Gate)
        {
            if (now >= _lastOpenUtc && now - _lastOpenUtc < TimeSpan.FromSeconds(3)) return false;
            _lastOpenUtc = now;
            return true;
        }
    }

    private static string Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var text = value.Trim().ToLowerInvariant().Replace('-', '_');
        return Field.IsMatch(text) ? text : "";
    }

    private static string? Str(JObject o, string key) => o[key]?.Type == JTokenType.String ? (string?)o[key] : null;

    private static void Post(string evt, string a, string b)
    {
        if (CoreSettings.Current.OfflineMode) return;
        var root = BaseUrlOverride ?? BackRoomApi.BaseUrl;
        if (!Uri.TryCreate(root.TrimEnd('/') + "/v2/public/funnel", UriKind.Absolute, out var uri)
            || !SandboxNet.Allows(uri)) return;
        var body = evt + "|panel" + (a.Length > 0 || b.Length > 0 ? "|" + a : "") + (b.Length > 0 ? "|" + b : "");
        var send = SendOverride;
        _ = Task.Run(async () =>
        {
            try
            {
                if (send != null) await send(uri.AbsoluteUri, body).ConfigureAwait(false);
                else
                {
                    using var content = new StringContent(body, Encoding.UTF8, "text/plain");
                    using var response = await Http.PostAsync(uri, content).ConfigureAwait(false);
                }
            }
            catch (Exception ex) { Log.Debug("[Funnel] beacon failed: {Error}", ex.Message); }
        });
    }

    internal static void ResetForTests()
    {
        lock (Gate) _lastOpenUtc = DateTime.MinValue;
        UtcNow = () => DateTime.UtcNow;
        OpenCheckout = ExternalOpener.Open;
        RefreshAccount = RefreshAccountAsync;
        CheckoutWatch.Disarm();
        SendOverride = null;
        BaseUrlOverride = null;
    }
}
