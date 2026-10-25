using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Browser;

/// <summary>WPF BrowserService popup policy: partner pages bypass ad filtering;
/// all other non-ad popups navigate the existing browser.</summary>
public static class BrowserPopupRule
{
    // ponytail: deliberate copy of WPF BrowserService.cs:31-71 and :676-698. The WPF head is the
    // frozen reference, so it keeps its own list; edit both if either changes.
    private static readonly HashSet<string> _blockedDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        // Major ad networks
        "doubleclick.net", "googlesyndication.com", "googleadservices.com",
        "google-analytics.com", "googletagmanager.com", "googletagservices.com",
        "adservice.google.com", "pagead2.googlesyndication.com",
        "adsense.google.com", "adnxs.com", "adsrvr.org",

        // Common ad/tracking domains
        "facebook.net", "fbcdn.net", "connect.facebook.net",
        "ads.twitter.com", "analytics.twitter.com",
        "advertising.com", "adform.net", "adroll.com",
        "criteo.com", "criteo.net", "outbrain.com", "taboola.com",
        "amazon-adsystem.com", "aax.amazon.com",
        "moatads.com", "adsafeprotected.com", "doubleverify.com",

        // Tracking pixels and analytics
        "quantserve.com", "scorecardresearch.com", "imrworldwide.com",
        "mixpanel.com", "segment.io", "segment.com", "amplitude.com",
        "hotjar.com", "fullstory.com", "mouseflow.com", "crazyegg.com",

        // Pop-up/pop-under networks
        "popads.net", "popcash.net", "propellerads.com", "exoclick.com",
        "trafficjunky.com", "trafficfactory.biz", "juicyads.com",
        "plugrush.com", "clickadu.com", "adsterra.com",
        "hilltopads.net", "pushame.com", "pushnami.com",

        // Adult ad networks (common on hypnotube etc)
        "exosrv.com", "realsrv.com", "tsyndicate.com", "syndication.exoclick.com",
        "a.realsrv.com", "syndication.realsrv.com", "mc.yandex.ru",
        "static.exoclick.com", "ads.exoclick.com",
        "ero-advertising.com", "eroads.com", "traffichaus.com",
        "awempire.com", "aweptjmp.com", "contentabc.com",

        // Malware/sketchy domains
        "malware-site.com", "adexchangegate.com", "adexchangetracker.com",

        // More tracking
        "newrelic.com", "nr-data.net", "onetrust.com",
        "cookielaw.org", "trustarc.com", "evidon.com",
        "bounceexchange.com", "bouncex.net"
    };

    public static bool NavigateInPlace(Uri? currentPage, Uri target)
    {
        if (!target.IsAbsoluteUri) return false;
        var host = currentPage?.IsAbsoluteUri == true ? currentPage.Host : "";
        if (host.Equals("hypnotube.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".hypnotube.com", StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var domain in _blockedDomains)
            if (target.Host.Equals(domain, StringComparison.OrdinalIgnoreCase)
                || target.Host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase)) return false;
        var url = target.AbsoluteUri.ToLowerInvariant();
        return !(url.Contains("/ads/") || url.Contains("/ad/") || url.Contains("doubleclick")
            || url.Contains("googlesyndication") || url.Contains("/popup")
            || url.Contains("clicktrack") || url.Contains("adserver"));
    }
}
