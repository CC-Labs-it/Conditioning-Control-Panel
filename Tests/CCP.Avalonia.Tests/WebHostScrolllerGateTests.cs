using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// WPF ScrolllerWebGate (main c45d58a65) on this head's one web seam: every hosted page's Scrolller
/// requests leave with the honest user-agent and our Referer; while the licence switch is off a
/// Scrolller page is never loaded (navigation cancelled, Navigate/Source refused). The in-app
/// browser (GateScrolller=False) is the user's own browsing and stays untouched, as in WPF.
/// </summary>
public sealed class WebHostScrolllerGateTests
{
    private static readonly Uri Page = new("https://scrolller.com/r/x");
    private static readonly Uri Media = new("https://images.scrolller.com/a/b.jpg");

    private static Task WithSwitch(bool enabled, Action body)
    {
        var (wasOn, wasCap) = (ScrolllerPolicy.Enabled, ScrolllerPolicy.MaxClipShortSide);
        ScrolllerPolicy.Apply(enabled, wasCap);
        return AvaloniaTestDispatcher.RunAsync(() =>
        {
            try { body(); }
            finally { ScrolllerPolicy.Apply(wasOn, wasCap); }
            return Task.CompletedTask;
        });
    }

    private static bool Cancelled(WebHost web, Uri target)
    {
        var e = new WebViewNavigationStartingEventArgs { Request = target };
        web.OnNavigationStarted(null, e);
        return e.Cancel;
    }

    [Fact]
    public Task SwitchOffNeverLoadsAScrolllerPage() => WithSwitch(false, () =>
    {
        var web = new WebHost();
        Assert.True(Cancelled(web, Page));
        Assert.Equal(1, web.ScrolllerRefusals);
        web.Navigate(Media);
        Assert.Equal(2, web.ScrolllerRefusals);
        Assert.Equal(1, web.RefusedNavigations);
        web.Navigate(new Uri("http://127.0.0.1:3001/"));          // not Scrolller: not this gate's refusal
        Assert.Equal(2, web.ScrolllerRefusals);
        Assert.False(web.GateScrolllerRequest(Media, _ => null, (_, _) => true));   // nothing to rewrite: never sent

        var browser = new WebHost { GateScrolller = false };     // the in-app browser
        Cancelled(browser, Page);
        Assert.Equal(0, browser.ScrolllerRefusals);
    });

    [Fact]
    public Task SwitchOnLetsScrolllerThrough() => WithSwitch(true, () =>
    {
        var web = new WebHost();
        Cancelled(web, Page);                                    // the test sandbox may still cancel it
        Assert.Equal(0, web.ScrolllerRefusals);
    });

    [Fact]
    public Task ScrolllerRequestsLeaveWithTheHonestUserAgent() => WithSwitch(true, () =>
    {
        var web = new WebHost();
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["User-Agent"] = "Mozilla/5.0 Edg/155", ["Referer"] = "https://reddit.com/r/x",
        };
        bool Set(string n, string v) { headers[n] = v; return true; }
        string? Get(string n) => headers.TryGetValue(n, out var v) ? v : null;

        Assert.True(web.GateScrolllerRequest(Media, Get, Set));
        Assert.Equal(ScrolllerSource.HonestUserAgent, headers["User-Agent"]);
        Assert.Equal("https://cclabs.app/", headers["Referer"]);
        Assert.Equal(1, web.ScrolllerRewrites);

        headers["Referer"] = "https://ccp.game/index.html";     // our own page's referer is kept
        Assert.True(web.GateScrolllerRequest(Media, Get, Set));
        Assert.Equal("https://ccp.game/index.html", headers["Referer"]);

        headers["User-Agent"] = "edge";
        Assert.False(web.GateScrolllerRequest(new Uri("https://notscrolller.com/a.jpg"), Get, Set));
        Assert.False(new WebHost { GateScrolller = false }.GateScrolllerRequest(Media, Get, Set));
        Assert.Equal("edge", headers["User-Agent"]);
    });

    /// <summary>The engine's own header collection, as the GTK/WebView2 adapters hand it over.</summary>
    private sealed class Headers : WebViewWebRequestHeaders
    {
        public readonly Dictionary<string, string> D = new(StringComparer.OrdinalIgnoreCase);
        public override int Count => D.Count;
        public override IEnumerable<string> Keys => D.Keys;
        public override IEnumerable<string> Values => D.Values;
        public override bool TrySet(string name, string value) { D[name] = value; return true; }
        public override bool TryRemove(string name) => D.Remove(name);
        public override bool ContainsKey(string key) => D.ContainsKey(key);
        public override bool TryGetValue(string key, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out string value) => D.TryGetValue(key, out value);
        public override IEnumerator<KeyValuePair<string, string>> GetEnumerator() => D.GetEnumerator();
    }

    [Fact]
    public Task TheEngineHandlerRewritesTheRequestItIsHanded() => WithSwitch(true, () =>
    {
        var web = new WebHost();
        var h = new Headers();
        (h.D["User-Agent"], h.D["Referer"]) = ("Mozilla/5.0 Edg/155", "https://reddit.com/r/x");
        web.StampRequestHeader(new WebResourceRequestedEventArgs
        {
            Request = new WebViewWebResourceRequest { Uri = Media, Method = HttpMethod.Get, Headers = h },
        });
        Assert.Equal(ScrolllerSource.HonestUserAgent, h.D["User-Agent"]);
        Assert.Equal("https://cclabs.app/", h.D["Referer"]);
        Assert.Equal(1, web.ScrolllerRewrites);
    });
}
