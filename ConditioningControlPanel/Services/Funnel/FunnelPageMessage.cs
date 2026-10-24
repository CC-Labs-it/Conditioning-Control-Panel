using System;
using System.Diagnostics;
using ConditioningControlPanel.Services.Vault;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Funnel;

/// <summary>What a game page asked for in one <c>funnel</c> message.</summary>
/// <param name="Url">The checkout to open, or null when the message only counts.</param>
public sealed record FunnelPageRequest(string Event, string A, string B, string? Url);

/// <summary>
/// The host side of a game page's end card (contract C10). A page posts
/// <c>{type:'funnel', event, a, b, plan?}</c> through <c>chrome.webview.postMessage</c>; the host
/// counts it through <see cref="FunnelBeacon"/> and, for a <c>cta_clicked</c> that names a
/// <c>plan</c> (<c>basic|prime</c>), opens that plan's checkout in the browser: the Patreon
/// checkout for rail <c>patreon</c> (the default), SubscribeStar for <c>substar</c>, the site
/// checkout tagged <c>from=panel</c> for <c>web</c>.
/// <para>The page never supplies a url: links come from <see cref="PlanCatalog"/>, so a page can
/// open nothing but the two checkouts. Opens are throttled (one per
/// <see cref="OpenEvery"/>) so a double-fired click opens one tab.</para>
/// </summary>
public static class FunnelPageMessage
{
    public const string Type = "funnel";
    public static readonly TimeSpan OpenEvery = TimeSpan.FromSeconds(3);

    private static DateTime _lastOpenUtc = DateTime.MinValue;
    private static readonly object Gate = new();

    /// <summary>Test seam: receives the url in place of the browser.</summary>
    internal static Action<string>? OpenOverride;

    /// <summary>
    /// The request in a page message, or null when it is not a funnel message or names an event
    /// the beacon does not count. <paramref name="plans"/> defaults to the cached plan list.
    /// </summary>
    public static FunnelPageRequest? Read(JObject? m, PlanSet? plans = null)
    {
        if (m == null || (string?)m["type"] != Type) return null;
        var evt = Str(m, "event");
        var a = FunnelBeacon.Clean(Str(m, "a"));
        var b = FunnelBeacon.Clean(Str(m, "b"));
        if (FunnelBeacon.Body(evt, a, b) == null) return null;
        string? url = null;
        if (evt == "cta_clicked" && (plans ?? PlanCatalog.Current).ForKey(Str(m, "plan")) is PlanInfo plan)
        {
            // Any rail other than web / substar opens Patreon, so it is counted as patreon too.
            if (b != "web" && b != "substar") b = "patreon";
            url = b switch
            {
                "web" => PlanCatalog.WebUrlFor(plan, "panel"),
                "substar" => plan.SubstarUrl,
                _ => plan.PatreonUrl,
            };
        }
        return new FunnelPageRequest(evt!, a, b, url);
    }

    /// <summary>
    /// Count the message and open its checkout. <paramref name="mayOpen"/> false (a run still on
    /// screen, a closing window, a test mode) counts without opening. Never throws.
    /// </summary>
    public static void Handle(JObject? m, bool mayOpen = true)
    {
        try
        {
            if (Read(m) is not FunnelPageRequest req) return;
            FunnelBeacon.Post(req.Event, req.A, req.B);
            if (req.Url == null || !mayOpen || !TakeOpenSlot(DateTime.UtcNow)) return;
            App.Logger?.Information("[Funnel] page opened {Url}", req.Url);
            CheckoutWatch.NoteOpened(req.Url);
            if (OpenOverride != null) { OpenOverride(req.Url); return; }
            Process.Start(new ProcessStartInfo { FileName = req.Url, UseShellExecute = true });
        }
        catch (Exception ex) { Diag.Swallowed(ex, "funnel page message"); }
    }

    /// <summary>True at most once per <see cref="OpenEvery"/>.</summary>
    internal static bool TakeOpenSlot(DateTime nowUtc)
    {
        lock (Gate)
        {
            if (nowUtc - _lastOpenUtc < OpenEvery && nowUtc >= _lastOpenUtc) return false;
            _lastOpenUtc = nowUtc;
            return true;
        }
    }

    /// <summary>Forget the throttle (tests).</summary>
    internal static void Reset()
    {
        lock (Gate) _lastOpenUtc = DateTime.MinValue;
        OpenOverride = null;
    }

    private static string? Str(JObject m, string key) => m[key]?.Type == JTokenType.String ? (string?)m[key] : null;
}
