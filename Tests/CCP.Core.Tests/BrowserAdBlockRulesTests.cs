using System;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Services.Browser;
using Xunit;

namespace CCP.Core.Tests;

public class BrowserAdBlockRulesTests
{
    [Fact]
    public void WebKitPatternsDoNotUseUnsupportedDisjunction()
    {
        using var json = JsonDocument.Parse(BrowserAdBlockRules.WebKitJson);
        foreach (var rule in json.RootElement.EnumerateArray())
        {
            var trigger = rule.GetProperty("trigger");
            Assert.DoesNotContain("|", trigger.GetProperty("url-filter").GetString()!);
            foreach (var pattern in trigger.GetProperty("unless-top-url").EnumerateArray())
                Assert.DoesNotContain("|", pattern.GetString()!);
        }
    }

    [Fact]
    public void WebKitRulesCoverEveryDomainAndSubdomainWithoutBlockingOrdinaryHosts()
    {
        using var json = JsonDocument.Parse(BrowserAdBlockRules.WebKitJson);
        var rules = json.RootElement.EnumerateArray().ToArray();
        Assert.Equal(78, BrowserPopupRule.BlockedDomains.Count);
        Assert.Equal(BrowserPopupRule.BlockedDomains.Count * 2, rules.Length);
        foreach (var domain in BrowserPopupRule.BlockedDomains)
        {
            bool Matches(string url) => rules.Any(r => Regex.IsMatch(url,
                r.GetProperty("trigger").GetProperty("url-filter").GetString()!, RegexOptions.IgnoreCase));
            Assert.True(Matches("https://" + domain), domain);
            Assert.True(Matches("https://" + domain + "?q=1"), domain);
            Assert.True(Matches("https://" + domain + "/ad"), domain);
            Assert.True(Matches("https://deep.sub." + domain + ":443/ad"), domain);
        }
        foreach (var rule in rules)
        {
            Assert.Equal("block", rule.GetProperty("action").GetProperty("type").GetString());
            var pattern = rule.GetProperty("trigger").GetProperty("url-filter").GetString()!;
            Assert.False(Regex.IsMatch("https://bambicloud.com/", pattern, RegexOptions.IgnoreCase));
            Assert.False(Regex.IsMatch("https://doubleclick.net.example.org/", pattern, RegexOptions.IgnoreCase));
        }
    }

    [Fact]
    public void EveryRuleExemptsOnlyHypnotubeAndItsSubdomains()
    {
        using var json = JsonDocument.Parse(BrowserAdBlockRules.WebKitJson);
        foreach (var rule in json.RootElement.EnumerateArray())
        {
            var exceptions = rule.GetProperty("trigger").GetProperty("unless-top-url");
            Assert.Equal(2, exceptions.GetArrayLength());
            bool Matches(string url) => exceptions.EnumerateArray().Any(e =>
                Regex.IsMatch(url, e.GetString()!, RegexOptions.IgnoreCase));
            Assert.True(Matches("https://hypnotube.com"));
            Assert.True(Matches("https://hypnotube.com/"));
            Assert.True(Matches("https://www.HYPNOTUBE.com/watch"));
            Assert.True(Matches("http://hypnotube.com:80/?q=1"));
            Assert.False(Matches("https://nothypnotube.com/"));
            Assert.False(Matches("https://hypnotube.com.example.org/"));
        }
    }
}
