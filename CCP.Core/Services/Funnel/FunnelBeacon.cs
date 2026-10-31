using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.Funnel;

/// <summary>
/// Aggregate funnel counters (contract C5 / C8): <c>POST /v2/public/funnel</c> with a
/// <c>text/plain</c> body <c>event|panel|a|b</c>. No account, no user id, no machine id: the server
/// only counts the line per day. Fire-and-forget: it never waits, never throws, never retries,
/// and does nothing when the proxy base url is empty.
/// <para>Events: <c>gate_shown</c> (a = feature key, b = basic|prime), <c>cta_clicked</c>
/// (a = feature key, b = patreon|substar|web), <c>checkout_started</c> (a = plan, b = rail),
/// <c>ref</c> (a = tag). Anything else is dropped here, before the wire.</para>
/// </summary>
public static class FunnelBeacon
{
    public const string Surface = "panel";
    public const string Path = "/v2/public/funnel";

    private static readonly HashSet<string> Events = new(StringComparer.Ordinal)
    {
        "gate_shown", "cta_clicked", "checkout_started", "ref",
    };

    private static readonly Regex Field = new("^[a-z0-9_]{1,32}$", RegexOptions.CultureInvariant);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    /// <summary>Test seam: receives (url, body) in place of the network.</summary>
    internal static Func<string, string, Task>? SendOverride;

    /// <summary>Test seam: replaces the proxy base url ("" = no proxy, the beacon stays quiet).</summary>
    internal static string? BaseUrlOverride;

    /// <summary>Count one event. Returns at once; every failure is swallowed.</summary>
    public static void Post(string evt, string a = "", string b = "")
    {
        try
        {
            var body = Body(evt, a, b);
            if (body == null) return;
            if (CoreSettings.Current.OfflineMode) return;
            var root = BaseUrlOverride ?? BackRoom.BackRoomApi.BaseUrl;
            if (string.IsNullOrWhiteSpace(root)) return;
            var url = root.TrimEnd('/') + Path;
            _ = Task.Run(async () =>
            {
                try
                {
                    if (SendOverride != null) { await SendOverride(url, body).ConfigureAwait(false); return; }
                    using var content = new StringContent(body, Encoding.UTF8, "text/plain");
                    using var res = await Http.PostAsync(url, content).ConfigureAwait(false);
                }
                catch (Exception ex) { Serilog.Log.Debug(ex, "Funnel beacon failed"); }
            });
        }
        catch (Exception ex) { Serilog.Log.Debug(ex, "Funnel beacon failed"); }
    }

    /// <summary>
    /// The wire line, or null when the event is not on the allowlist. Fields are lowercased,
    /// '-' becomes '_', and a field that still is not <c>[a-z0-9_]{1,32}</c> is sent empty.
    /// Trailing empty fields are dropped: <c>gate_shown|panel|lockdown|basic</c>, <c>ref|panel|x</c>.
    /// </summary>
    public static string? Body(string? evt, string? a = "", string? b = "")
    {
        if (evt == null || !Events.Contains(evt)) return null;
        var fa = Clean(a);
        var fb = Clean(b);
        var line = evt + "|" + Surface;
        if (fa.Length > 0 || fb.Length > 0) line += "|" + fa;
        if (fb.Length > 0) line += "|" + fb;
        return line;
    }

    /// <summary>A field as the server accepts it, or "".</summary>
    internal static string Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var v = value.Trim().ToLowerInvariant().Replace('-', '_');
        return Field.IsMatch(v) ? v : "";
    }

    /// <summary>"basic" for tier 1 and below, "prime" for tier 2 and up.</summary>
    public static string TierName(int tier) => tier >= 2 ? "prime" : "basic";
}
