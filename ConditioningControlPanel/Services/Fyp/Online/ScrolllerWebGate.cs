using System;
using System.Runtime.CompilerServices;
using Microsoft.Web.WebView2.Core;

namespace ConditioningControlPanel.Services.Fyp.Online;

/// <summary>What the gate does with one WebView2 request.</summary>
internal enum ScrolllerWebDecision
{
    /// <summary>Not a Scrolller host: left alone.</summary>
    Pass,
    /// <summary>Scrolller host while the licence switch is off: answered locally with an empty 403.</summary>
    Block,
    /// <summary>Scrolller host while on: goes out with the app's honest user-agent.</summary>
    Rewrite,
}

/// <summary>
/// The pages (Goon, DtRH, Arcademy, Intake, chess, Back Room, For You, the players...) fetch
/// Scrolller from inside WebView2, so without this every request wears the Edge user-agent.
/// <see cref="Attach"/> hooks one CoreWebView2: a Scrolller request goes out as
/// <see cref="ScrolllerSource.HonestUserAgent"/>, or never leaves the machine while
/// <see cref="ScrolllerPolicy.Enabled"/> is off (owner, 2026-10-18, Scrolller licence).
/// Probed on WebView2 runtime 155 (SDK 1.0.2535.41): a User-Agent set here reaches the server for
/// images, fetch, media and worker fetches; RemoveHeader("Referer") is ignored but SetHeader
/// overwrites it, so a foreign Referer is replaced with ours.
/// </summary>
internal static class ScrolllerWebGate
{
    internal const string OurReferer = "https://cclabs.app/";

    private static readonly ConditionalWeakTable<CoreWebView2, object> Attached = new();

    /// <summary>scrolller.com or any subdomain of it; never a lookalike such as notscrolller.com.</summary>
    internal static bool IsScrolllerHost(string? host)
    {
        if (string.IsNullOrEmpty(host)) return false;
        var h = host.TrimEnd('.');
        return h.Equals("scrolller.com", StringComparison.OrdinalIgnoreCase)
            || h.EndsWith(".scrolller.com", StringComparison.OrdinalIgnoreCase);
    }

    internal static ScrolllerWebDecision Decide(string? requestUri, bool enabled)
    {
        if (string.IsNullOrEmpty(requestUri)) return ScrolllerWebDecision.Pass;
        if (!Uri.TryCreate(requestUri, UriKind.Absolute, out var uri)) return ScrolllerWebDecision.Pass;
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp
            && uri.Scheme != "wss" && uri.Scheme != "ws") return ScrolllerWebDecision.Pass;
        if (!IsScrolllerHost(uri.Host)) return ScrolllerWebDecision.Pass;
        return enabled ? ScrolllerWebDecision.Rewrite : ScrolllerWebDecision.Block;
    }

    /// <summary>A Referer is ours when it names cclabs.app (or a subdomain) or one of the app's own
    /// virtual hosts (ccp.game, ccp.assets, leash-video.ccp...). Missing counts as ours.</summary>
    internal static bool IsOurReferer(string? referer)
    {
        if (string.IsNullOrEmpty(referer)) return true;
        if (!Uri.TryCreate(referer, UriKind.Absolute, out var uri)) return false;
        var h = uri.Host;
        return h.Equals("cclabs.app", StringComparison.OrdinalIgnoreCase)
            || h.EndsWith(".cclabs.app", StringComparison.OrdinalIgnoreCase)
            || h.StartsWith("ccp.", StringComparison.OrdinalIgnoreCase)
            || h.EndsWith(".ccp", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Hooks the gate onto a CoreWebView2. Safe to call twice on the same core; never
    /// throws (a host that cannot be gated still runs, and logs why).</summary>
    public static void Attach(CoreWebView2? core)
    {
        if (core == null) return;
        try
        {
            if (Attached.TryGetValue(core, out _)) return;
            Attached.Add(core, new object());

            foreach (var pattern in new[] { "*://scrolller.com/*", "*://*.scrolller.com/*" })
            {
                try
                {
                    // Source kinds All also catches workers and iframes (runtime 1.0.2210+).
                    core.AddWebResourceRequestedFilter(pattern, CoreWebView2WebResourceContext.All,
                        CoreWebView2WebResourceRequestSourceKinds.All);
                }
                catch (Exception ex) when (ex is NotImplementedException or InvalidCastException
                                               or NotSupportedException)
                {
                    core.AddWebResourceRequestedFilter(pattern, CoreWebView2WebResourceContext.All);
                }
            }

            core.WebResourceRequested += (_, e) => Handle(core, e);
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("ScrolllerWebGate: attach failed: {E}", ex.Message);
        }
    }

    private static void Handle(CoreWebView2 core, CoreWebView2WebResourceRequestedEventArgs e)
    {
        try
        {
            switch (Decide(e.Request.Uri, ScrolllerPolicy.Enabled))
            {
                case ScrolllerWebDecision.Block:
                    e.Response = core.Environment.CreateWebResourceResponse(null, 403, "Forbidden", "");
                    break;
                case ScrolllerWebDecision.Rewrite:
                    var headers = e.Request.Headers;
                    headers.SetHeader("User-Agent", ScrolllerSource.HonestUserAgent);
                    if (headers.Contains("Referer") && !IsOurReferer(headers.GetHeader("Referer")))
                        headers.SetHeader("Referer", OurReferer);
                    break;
            }
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("ScrolllerWebGate: request hook failed: {E}", ex.Message);
        }
    }
}
