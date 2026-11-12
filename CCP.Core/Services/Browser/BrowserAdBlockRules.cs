using System;
using System.Linq;
using System.Text.Json;

namespace ConditioningControlPanel.Services.Browser;

/// <summary>WebKit request rules for the WPF domain list and partner exemption
/// (BrowserService.cs:31-82, 580-640). Installation is the host's responsibility.</summary>
public static class BrowserAdBlockRules
{
    public static bool ShouldBlock(string? pageUrl, string? requestUrl) =>
        !BrowserPopupRule.IsPartner(pageUrl)
        && Uri.TryCreate(requestUrl, UriKind.Absolute, out var target)
        && BrowserPopupRule.IsBlockedHost(target.Host);

    // WebKit does not support regex alternation; emit separate boundary and end-of-URL patterns.
    private const string PartnerUrl = @"^https?://([^/:]+\.)?hypnotube\.com";
    private static readonly string[] PartnerUrls = { PartnerUrl + "[/:?#]", PartnerUrl + "$" };

    public static string WebKitJson { get; } = JsonSerializer.Serialize(
        BrowserPopupRule.BlockedDomains.SelectMany(domain => new[] { "[/:?#]", "$" }.Select(end => new
        {
            trigger = new
            {
                url_filter = @"^[^:]+://+([^/:]+\.)?" + domain.Replace(".", @"\.") + end,
                unless_top_url = PartnerUrls
            },
            action = new { type = "block" }
        })).Select(rule => new System.Collections.Generic.Dictionary<string, object>
        {
            ["trigger"] = new System.Collections.Generic.Dictionary<string, object>
            {
                ["url-filter"] = rule.trigger.url_filter,
                ["unless-top-url"] = rule.trigger.unless_top_url
            },
            ["action"] = rule.action
        }));
}
