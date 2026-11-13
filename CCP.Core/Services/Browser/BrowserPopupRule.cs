using System;
namespace ConditioningControlPanel.Services.Browser;
/// <summary>Popup policy from WPF Services/Browser/BrowserService.cs:31-73, 538-562, 627-707.
/// Partner documents bypass ad checks; all other allowed popups stay in the existing view.</summary>
public static class BrowserPopupRule
{
    private static readonly string[] _blockedDomains =
    {
        "doubleclick.net", "googlesyndication.com", "googleadservices.com", "google-analytics.com", "googletagmanager.com", "googletagservices.com", "adservice.google.com", "pagead2.googlesyndication.com", "adsense.google.com", "adnxs.com",
        "adsrvr.org", "facebook.net", "fbcdn.net", "connect.facebook.net", "ads.twitter.com", "analytics.twitter.com", "advertising.com", "adform.net", "adroll.com", "criteo.com",
        "criteo.net", "outbrain.com", "taboola.com", "amazon-adsystem.com", "aax.amazon.com", "moatads.com", "adsafeprotected.com", "doubleverify.com", "quantserve.com", "scorecardresearch.com",
        "imrworldwide.com", "mixpanel.com", "segment.io", "segment.com", "amplitude.com", "hotjar.com", "fullstory.com", "mouseflow.com", "crazyegg.com", "popads.net",
        "popcash.net", "propellerads.com", "exoclick.com", "trafficjunky.com", "trafficfactory.biz", "juicyads.com", "plugrush.com", "clickadu.com", "adsterra.com", "hilltopads.net",
        "pushame.com", "pushnami.com", "exosrv.com", "realsrv.com", "tsyndicate.com", "syndication.exoclick.com", "a.realsrv.com", "syndication.realsrv.com", "mc.yandex.ru", "static.exoclick.com",
        "ads.exoclick.com", "ero-advertising.com", "eroads.com", "traffichaus.com", "awempire.com", "aweptjmp.com", "contentabc.com", "malware-site.com", "adexchangegate.com", "adexchangetracker.com",
        "newrelic.com", "nr-data.net", "onetrust.com", "cookielaw.org", "trustarc.com", "evidon.com", "bounceexchange.com", "bouncex.net"
    };
    public static bool ShouldBlock(string? currentUrl, string targetUrl)
    {
        if (Uri.TryCreate(currentUrl, UriKind.Absolute, out var page)
            && (page.Host.Equals("hypnotube.com", StringComparison.OrdinalIgnoreCase)
                || page.Host.EndsWith(".hypnotube.com", StringComparison.OrdinalIgnoreCase)))
            return false;
        if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out var target)) return false;
        foreach (var domain in _blockedDomains)
            if (target.Host.Equals(domain, StringComparison.OrdinalIgnoreCase)
                || target.Host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase)) return true;
        var lower = targetUrl.ToLowerInvariant();
        return lower.Contains("/ads/") || lower.Contains("/ad/") || lower.Contains("doubleclick")
            || lower.Contains("googlesyndication") || lower.Contains("/popup")
            || lower.Contains("clicktrack") || lower.Contains("adserver");
    }
}
