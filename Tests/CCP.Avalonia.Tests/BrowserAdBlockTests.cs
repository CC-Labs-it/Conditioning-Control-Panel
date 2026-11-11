using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using Xunit;

namespace CCP.Avalonia.Tests;

public class BrowserAdBlockTests
{
    [Fact]
    public Task HomeRequestsFilterAndWaitsForItsReport() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var home = new WebHost { BlockAds = true };
        var other = new WebHost();
        Action<bool>? report = null;
        bool? requested = null;
        void Attach(IntPtr _, bool ads, Action<bool> done) { requested = ads; report = done; }
        var page = new Uri("https://bambicloud.com/");
        var ready = home.NewScrolllerEngine();
        ScrolllerWebHostGate.AttachLinuxFilter(IntPtr.Zero, home.BlockAds, ready, Attach);
        Assert.True(requested);
        Assert.False(WebHost.ScrolllerGateAllows(home.ScrolllerGate, page));
        report!(true);
        Assert.True(WebHost.ScrolllerGateAllows(home.ScrolllerGate, page));
        ScrolllerWebHostGate.AttachLinuxFilter(IntPtr.Zero, other.BlockAds, _ => { }, Attach);
        Assert.False(requested);
        return Task.CompletedTask;
    });

    [Fact]
    public Task HostBlocksOnlyDomainRequestsOutsidePartnerPages() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var home = new WebHost { BlockAds = true };
        var other = new WebHost();
        Assert.True(home.ShouldBlockAdRequest("https://bambicloud.com/", "https://x.doubleclick.net/ad"));
        Assert.False(home.ShouldBlockAdRequest("https://www.hypnotube.com/", "https://x.doubleclick.net/ad"));
        Assert.True(home.ShouldBlockAdRequest("https://nothypnotube.com/", "https://doubleclick.net/"));
        Assert.False(home.ShouldBlockAdRequest("https://bambicloud.com/", "https://example.org/ads/"));
        Assert.False(other.ShouldBlockAdRequest("https://bambicloud.com/", "https://doubleclick.net/"));
        return Task.CompletedTask;
    });

    [Fact]
    public void OnlyTheOptedInFilterContainsTheAdRules()
    {
        using var normal = JsonDocument.Parse(ScrolllerWebKitFilter.RulesFor(false));
        using var home = JsonDocument.Parse(ScrolllerWebKitFilter.RulesFor(true));
        Assert.Equal(1, normal.RootElement.GetArrayLength());
        Assert.Equal(79, home.RootElement.GetArrayLength());
        Assert.Contains("hypnotube", home.RootElement.GetRawText());
        Assert.DoesNotContain("hypnotube", normal.RootElement.GetRawText());
    }

    [Fact]
    public void OnlyHomeOptsInAndGatePassesTheFlagToTheNativeFilter()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !Directory.Exists(Path.Combine(root.FullName, "CCP.Avalonia", "Views"))) root = root.Parent;
        Assert.NotNull(root);
        var avalonia = Path.Combine(root!.FullName, "CCP.Avalonia");
        var files = Directory.EnumerateFiles(Path.Combine(avalonia, "Views"), "*.axaml", SearchOption.AllDirectories);
        var optedIn = files.Where(f => Regex.IsMatch(File.ReadAllText(f), @"<controls:WebHost\b[^>]*BlockAds\s*=\s*""True""", RegexOptions.IgnoreCase)).ToArray();
        Assert.Single(optedIn);
        Assert.EndsWith("SettingsTabView.axaml", optedIn[0]);
        var gate = File.ReadAllText(Path.Combine(avalonia, "Platform", "ScrolllerWebHostGate.cs"));
        Assert.Contains("AttachLinuxFilter(g.WebKitWebView, blockAds", gate);
        Assert.Contains("AttachAdWebView2(core, shouldBlockAds!)", gate);
        var windows = File.ReadAllText(Path.Combine(avalonia, "Platform", "ScrolllerWebView2Gate.cs"));
        Assert.Contains("shouldBlockAds(core.Source, e.Request.Uri)", windows);
        Assert.Contains("CreateWebResourceResponse(null, 204", windows);
    }
}
