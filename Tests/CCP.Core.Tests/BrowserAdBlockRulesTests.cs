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
    public void WebKitRulesCoverEveryDomainAndSubdomainWithoutBlockingOrdinaryHosts()
    {
        using var json = JsonDocument.Parse(BrowserAdBlockRules.WebKitJson);
        var rules = json.RootElement.EnumerateArray().ToArray();
        Assert.Equal(78, BrowserPopupRule.BlockedDomains.Count);
        Assert.Equal(BrowserPopupRule.BlockedDomains.Count, rules.Length);
        foreach (var domain in BrowserPopupRule.BlockedDomains)
        {
            bool Matches(string url) => rules.Any(r => Regex.IsMatch(url,
                r.GetProperty("trigger").GetProperty("url-filter").GetString()!, RegexOptions.IgnoreCase));
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
            Assert.Equal(1, exceptions.GetArrayLength());
            var pattern = exceptions[0].GetString()!;
            Assert.True(Regex.IsMatch("https://hypnotube.com/", pattern, RegexOptions.IgnoreCase));
            Assert.True(Regex.IsMatch("https://www.HYPNOTUBE.com/watch", pattern, RegexOptions.IgnoreCase));
            Assert.False(Regex.IsMatch("https://nothypnotube.com/", pattern, RegexOptions.IgnoreCase));
            Assert.False(Regex.IsMatch("https://hypnotube.com.example.org/", pattern, RegexOptions.IgnoreCase));
        }
    }
}
