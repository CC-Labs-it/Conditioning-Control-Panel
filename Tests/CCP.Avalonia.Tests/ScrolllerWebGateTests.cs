using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// The Scrolller gate on every web view of this head. Windows is WPF's own ScrolllerWebGate (moved to Core; its rules are
/// pinned by the WPF suite), hooked on the core behind each WebHost; Linux is a WebKit block filter. Here: the first
/// navigation waits for the gate, an engine without one loads only this head's own pages, the Linux rule, the live cache
/// clears on the sweep and the off switch, and the source scan that keeps a new web view from shipping without the gate.
/// </summary>
public sealed class ScrolllerWebGateTests
{
    [Theory]
    [InlineData(null, "https://example.test/", false)]          // not reported yet: held
    [InlineData(false, "https://scrolller.com/r/x", false)]     // no gate: never a remote page
    [InlineData(false, "https://cclabs.app/", false)]
    [InlineData(false, "http://127.0.0.1:5555/fyp/index.html", true)]   // this head's own pages (relay only)
    [InlineData(null, "about:blank", true)]
    [InlineData(true, "https://scrolller.com/r/x", true)]       // gated: the gate answers every request
    public void An_engine_without_a_gate_loads_only_this_heads_own_pages(bool? gate, string url, bool allowed)
        => Assert.Equal(allowed, WebHost.ScrolllerGateAllows(gate, new Uri(url)));

    [Fact]
    public void The_first_navigation_waits_for_the_gate()
    {
        var host = File.ReadAllText(Path.Combine(RepoRoot(), "CCP.Avalonia", "Views", "Controls", "WebHost.axaml.cs"));
        Assert.Contains("Platform.ScrolllerWebHostGate.Attach(_web, OnScrolllerGate);", host);
        // Both roads into the engine (Source and Navigate) return while the gate has not reported.
        Assert.Equal(2, Regex.Matches(host, @"if \(_scrolllerGate is null\) return;").Count);
        Assert.Contains("if (!ScrolllerGateAllows(_scrolllerGate, target))", host);
    }

    [Fact]
    public void A_missing_or_unknown_handle_reports_no_gate_once()
    {
        var reports = new List<bool>();
        ScrolllerWebHostGate.OnAdapter(null, () => null, reports.Add);
        ScrolllerWebHostGate.OnAdapter(new global::Avalonia.Platform.PlatformHandle(IntPtr.Zero, "test"), () => null, reports.Add);
        Assert.Equal(new[] { false, false }, reports);
    }

    [Fact]
    public void A_zero_core_is_not_attached()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.False(ScrolllerWebView2Gate.Attach(IntPtr.Zero));
    }

    [Fact]
    public void Windows_is_wpfs_own_gate_and_the_sweeps_clear_the_live_cores()
    {
        var root = RepoRoot();
        Assert.True(File.Exists(Path.Combine(root, "CCP.Core", "Services", "Fyp", "Online", "ScrolllerWebGate.cs")));
        Assert.False(File.Exists(Path.Combine(root, "ConditioningControlPanel", "Services", "Fyp", "Online", "ScrolllerWebGate.cs")));
        Assert.Contains("ScrolllerWebGate.Attach(CoreWebView2.CreateFromComICoreWebView2(coreWebView2))",
            File.ReadAllText(Path.Combine(root, "CCP.Avalonia", "Platform", "ScrolllerWebView2Gate.cs")));
        var head = File.ReadAllText(Path.Combine(root, "CCP.Avalonia", "Platform", "ScrolllerHead.cs"));
        Assert.Contains("ScrolllerWebGate.ClearDueChromiumCachesAsync()", head);   // hourly
        Assert.Contains("ScrolllerWebGate.ClearChromiumCachesAsync(reason)", head);  // the off switch
    }

    [Theory]
    [InlineData("https://scrolller.com/", true)]
    [InlineData("https://scrolller.com:443/r/x", true)]
    [InlineData("https://images.scrolller.com/a/b.mp4", true)]
    [InlineData("wss://live.scrolller.com/socket", true)]
    [InlineData("https://notscrolller.com/a.jpg", false)]
    [InlineData("https://scrolller.com.evil.test/a.jpg", false)]
    [InlineData("http://127.0.0.1:5555/scrolller-media/images/a/b.jpg", false)]   // the relay is this head's own
    [InlineData("https://cclabs.app/", false)]
    public void The_linux_block_rule_covers_scrolller_and_nothing_else(string url, bool blocked)
    {
        var rules = JArray.Parse(ScrolllerWebKitFilter.RulesJson);
        var rule = Assert.Single(rules);
        Assert.Equal("block", (string?)rule["action"]!["type"]);
        var filter = (string)rule["trigger"]!["url-filter"]!;
        Assert.Equal(blocked, Regex.IsMatch(url, filter, RegexOptions.IgnoreCase));
    }

    [Fact]
    public void The_stored_linux_filter_is_named_after_its_rule()
        => Assert.Matches("^ccp-scrolller-block-[0-9a-f]{12}$", ScrolllerWebKitFilter.Identifier);

    [Fact]
    public void Every_web_view_is_a_webhost_and_every_webhost_is_gated()
    {
        var root = RepoRoot();
        var avalonia = Path.Combine(root, "CCP.Avalonia");
        var builders = Directory.EnumerateFiles(avalonia, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"new\s+(global::Avalonia\.Controls\.)?NativeWebView\s*\("))
            .Select(f => Path.GetRelativePath(avalonia, f).Replace('\\', '/')).ToList();
        Assert.Equal(new[] { "Views/Controls/WebHost.axaml.cs" }, builders);
        var gate = File.ReadAllText(Path.Combine(avalonia, "Platform", "ScrolllerWebHostGate.cs"));
        Assert.Contains("ScrolllerWebView2Gate.Attach(", gate);
        Assert.Contains("ScrolllerWebKitFilter.Attach(", gate);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "CCP.Avalonia", "Platform"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
