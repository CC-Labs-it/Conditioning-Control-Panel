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

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }
}
