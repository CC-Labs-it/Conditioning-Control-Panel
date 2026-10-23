using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// WebView2 pages fetch Scrolller too (owner, 2026-10-18, Scrolller licence): a Scrolller request
/// leaves with the app's own user-agent, or never leaves while the licence switch is off. Only
/// scrolller.com and its subdomains count; nothing else is touched.
/// </summary>
public class ScrolllerWebGateTests
{
    // The gate's filter makes JustDrop's handler see Scrolller requests too: the CCP token goes on
    // the sign-in request only, judged on the parsed url.
    [Theory]
    [InlineData("https://app.cclabs.app/api/auth/desktop-session", true)]
    [InlineData("https://app.cclabs.app/api/auth/desktop-session?next=%2Fjustdrop", true)]
    [InlineData("https://api.scrolller.com/admin", false)]
    [InlineData("https://app.cclabs.app/api/auth/desktop-session-x", false)]
    [InlineData("https://app.cclabs.app/api/auth/other", false)]
    [InlineData("https://app.cclabs.app@evil.net/api/auth/desktop-session", false)]
    [InlineData("https://user@app.cclabs.app/api/auth/desktop-session", false)]
    [InlineData("http://app.cclabs.app/api/auth/desktop-session", false)]
    [InlineData("https://app.cclabs.app:8443/api/auth/desktop-session", false)]
    [InlineData("not a url", false)]
    public void JustDrop_token_only_rides_the_sign_in_request(string url, bool expected)
        => Assert.Equal(expected, ConditioningControlPanel.Services.JustDrop.JustDropHostService.IsDesktopSessionRequest(url));

    [Theory]
    [InlineData("scrolller.com")]
    [InlineData("api.scrolller.com")]
    [InlineData("images.scrolller.com")]
    [InlineData("a.b.scrolller.com")]
    [InlineData("SCROLLLER.COM")]
    [InlineData("scrolller.com.")]
    public void Matches_scrolller_and_its_subdomains(string host)
        => Assert.True(ScrolllerWebGate.IsScrolllerHost(host));

    [Theory]
    [InlineData("notscrolller.com")]
    [InlineData("scrolller.com.evil.net")]
    [InlineData("scroller.com")]
    [InlineData("xscrolller.com")]
    [InlineData("cclabs.app")]
    [InlineData("")]
    [InlineData(null)]
    public void Never_matches_a_lookalike(string? host)
        => Assert.False(ScrolllerWebGate.IsScrolllerHost(host));

    [Theory]
    [InlineData("https://api.scrolller.com/api/v2/graphql", true, "Rewrite")]
    [InlineData("https://images.scrolller.com/a/b.mp4", true, "Rewrite")]
    [InlineData("https://api.scrolller.com/api/v2/graphql", false, "Block")]
    [InlineData("http://scrolller.com/", false, "Block")]
    [InlineData("https://notscrolller.com/x", true, "Pass")]
    [InlineData("https://notscrolller.com/x", false, "Pass")]
    [InlineData("https://ccp.game/index.html", false, "Pass")]
    [InlineData("https://example.com/?u=https://scrolller.com/", false, "Pass")]
    [InlineData("data:text/plain,scrolller.com", false, "Pass")]
    [InlineData("not a url", false, "Pass")]
    [InlineData(null, false, "Pass")]
    public void Decides_block_rewrite_or_pass(string? uri, bool enabled, string expected)
        => Assert.Equal(expected, ScrolllerWebGate.Decide(uri, enabled).ToString());

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("https://cclabs.app/", true)]
    [InlineData("https://app.cclabs.app/race", true)]
    [InlineData("https://ccp.game/", true)]
    [InlineData("https://leash-video.ccp/", true)]
    [InlineData("https://scrolller.com/", false)]
    [InlineData("https://www.scrolller.com/r/x", false)]
    [InlineData("https://notcclabs.app/", false)]
    [InlineData("garbage", false)]
    public void Keeps_only_our_own_referer(string? referer, bool ours)
        => Assert.Equal(ours, ScrolllerWebGate.IsOurReferer(referer));

    [Fact]
    public void Our_referer_replacement_is_ours()
        => Assert.True(ScrolllerWebGate.IsOurReferer(ScrolllerWebGate.OurReferer));

    /// <summary>A new WebView2 host must hook the gate, or its pages reach Scrolller as Edge.
    /// The in-app web browser is the user's own browsing and is left alone on purpose.</summary>
    [Fact]
    public void Every_webview_host_attaches_the_gate()
    {
        var app = Path.Combine(RepoRoot(), "ConditioningControlPanel");
        var exempt = new[] { Path.Combine("Services", "Browser", "BrowserService.cs") };
        var missing = Directory.EnumerateFiles(app, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                     && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .Where(f => File.ReadAllLines(f).Any(l => l.Contains(".EnsureCoreWebView2Async(")
                                                     && !l.TrimStart().StartsWith("//")))
            .Where(f => !exempt.Any(e => f.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
            .Where(f => !File.ReadAllText(f).Contains("ScrolllerWebGate.Attach("))
            .Select(f => Path.GetRelativePath(app, f))
            .ToList();
        Assert.True(missing.Count == 0, "WebView2 hosts without ScrolllerWebGate.Attach: " + string.Join(", ", missing));
    }

    // ---- Chromium caches under the day limit (owner, 2026-10-21) -------------------------

    [Fact]
    public void Gate_answers_never_enter_chromiums_disk_cache()
    {
        Assert.Contains("Cache-Control: no-store\r\n", ScrolllerWebGate.ResponseHeaders("image/jpeg", 10));
        Assert.Contains("Cache-Control: no-store\r\n",
            ScrolllerWebGate.ResponseHeaders("video/mp4", 100, new ScrolllerByteRange(0, 99), 1000));
    }

    [Theory]
    [InlineData(null, true)]      // never cleared
    [InlineData(1.0, false)]
    [InlineData(22.9, false)]
    [InlineData(23.0, true)]      // a day old before the next hourly sweep
    [InlineData(24.0, true)]
    [InlineData(72.0, true)]
    [InlineData(-2.0, true)]      // stamp from the future: the clock went back
    public void Chromium_clear_due_matrix(double? hoursAgo, bool due)
    {
        var now = new DateTime(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);
        DateTime? last = hoursAgo is double h ? now.AddHours(-h) : null;
        Assert.Equal(due, ScrolllerWebGate.ChromiumClearDue(last, now));
    }

    [Fact]
    public void Clear_stamps_round_trip_by_profile_name()
    {
        var at = new DateTime(2026, 10, 21, 9, 30, 0, DateTimeKind.Utc);
        Assert.Equal("browser_data_fyp", ScrolllerWebGate.ProfileKey(@"C:\Users\x\AppData\Roaming\CCP\browser_data_fyp\"));
        var json = ScrolllerWebGate.FormatClears(new System.Collections.Generic.Dictionary<string, ScrolllerWebGate.ClearStamp>
        {
            ["browser_data_goon"] = new(at, @"C:\ccp\browser_data_goon"),
        });
        var back = ScrolllerWebGate.ParseClears(json);
        Assert.Equal(at, back["BROWSER_DATA_GOON"].At);
        Assert.Equal(@"C:\ccp\browser_data_goon", back["browser_data_goon"].Folder);
        Assert.Empty(ScrolllerWebGate.ParseClears("not json"));
    }

    [Fact]
    public void A_closed_profile_loses_its_disk_cache_and_keeps_the_rest()
    {
        var udf = Path.Combine(Path.GetTempPath(), "ccp-udf-" + Guid.NewGuid().ToString("N"));
        try
        {
            var cache = Path.Combine(udf, "EBWebView", "Default", "Cache", "Cache_Data");
            var storage = Path.Combine(udf, "EBWebView", "Default", "Local Storage");
            Directory.CreateDirectory(cache);
            Directory.CreateDirectory(storage);
            File.WriteAllText(Path.Combine(cache, "f_000001"), "x");
            Assert.True(ScrolllerWebGate.DeleteClosedProfileCache(udf));
            Assert.False(Directory.Exists(Path.Combine(udf, "EBWebView", "Default", "Cache")));
            Assert.True(Directory.Exists(storage));
            Assert.False(ScrolllerWebGate.DeleteClosedProfileCache("relative\\path"));
        }
        finally { try { Directory.Delete(udf, true); } catch { } }
    }

    /// <summary>Every host that attaches the gate gets its profile's disk cache cleared, because the
    /// clear is wired inside Attach itself, not in each host. The host list is derived from the
    /// source, so a new host is covered (and counted) without editing this test.</summary>
    [Fact]
    public void Every_attached_host_is_covered_by_the_cache_clear()
    {
        var app = Path.Combine(RepoRoot(), "ConditioningControlPanel");
        var hosts = Directory.EnumerateFiles(app, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                     && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .Where(f => File.ReadAllLines(f).Any(l => l.Contains("ScrolllerWebGate.Attach(") && !l.TrimStart().StartsWith("//")))
            .ToList();
        Assert.NotEmpty(hosts);

        var gate = File.ReadAllText(Path.Combine(app, "Services", "Fyp", "Online", "ScrolllerWebGate.cs"));
        int start = gate.IndexOf("public static void Attach(CoreWebView2? core)", StringComparison.Ordinal);
        Assert.True(start >= 0, "Attach not found");
        int end = gate.IndexOf("private static void Handle(", start, StringComparison.Ordinal);
        var body = gate.Substring(start, end - start);
        Assert.Contains("TrackForCacheClears(core);", body);
        // The clear is the gate's only hook: no host may register the Scrolller filter itself.
        var selfFiltered = hosts.Where(f => File.ReadAllText(f).Contains("*.scrolller.com"))
            .Select(f => Path.GetRelativePath(app, f)).ToList();
        Assert.True(selfFiltered.Count == 0, "hosts with their own Scrolller filter: " + string.Join(", ", selfFiltered));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }
}
