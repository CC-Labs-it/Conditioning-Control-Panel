using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Browser;

/// <summary>WPF BrowserService popup policy: partner bypass, otherwise reject known ads.</summary>
public static class BrowserPopupRules
{
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

        /// <summary>
        /// Sites we have a partnership with - ad blocking is fully bypassed
        /// on these domains and their subdomains so we don't suppress their
        /// ad revenue inside the embedded browser.
        /// </summary>
        private static readonly HashSet<string> _partnerSites = new(StringComparer.OrdinalIgnoreCase)
        {
            "hypnotube.com",
        };

    public static bool Allows(Uri? current, Uri? target) =>
        target is { IsAbsoluteUri: true } &&
        (current is { IsAbsoluteUri: true } && IsPartnerSite(current.Host) || !IsAdUrl(target.AbsoluteUri));

        private static bool IsBlockedDomain(string host)
        {
            // Direct match
            if (_blockedDomains.Contains(host))
                return true;

            // Check if it's a subdomain of a blocked domain
            foreach (var blocked in _blockedDomains)
            {
                if (host.EndsWith("." + blocked, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Check if a host belongs to a partner site (or any of its subdomains).
        /// </summary>
        private static bool IsPartnerSite(string host)
        {
            if (_partnerSites.Contains(host))
                return true;

            foreach (var partner in _partnerSites)
            {
                if (host.EndsWith("." + partner, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool IsAdUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;

            try
            {
                var uri = new Uri(url);
                if (IsBlockedDomain(uri.Host.ToLowerInvariant()))
                    return true;

                // Check for common ad URL patterns
                var lowerUrl = url.ToLowerInvariant();
                return lowerUrl.Contains("/ads/") ||
                       lowerUrl.Contains("/ad/") ||
                       lowerUrl.Contains("doubleclick") ||
                       lowerUrl.Contains("googlesyndication") ||
                       lowerUrl.Contains("/popup") ||
                       lowerUrl.Contains("clicktrack") ||
                       lowerUrl.Contains("adserver");
            }
            catch
            {
                return false;
            }
        }

}
