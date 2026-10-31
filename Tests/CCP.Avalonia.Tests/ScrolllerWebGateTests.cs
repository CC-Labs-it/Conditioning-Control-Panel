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
        Assert.Contains("Platform.ScrolllerWebHostGate.Attach(_web, NewScrolllerEngine, OnWindowsCore);", host);
        // Both roads into the engine (Source and Navigate) return while the gate has not reported.
        Assert.Equal(2, Regex.Matches(host, @"if \(_scrolllerGate is null\) return;").Count);
        Assert.Contains("if (!ScrolllerGateAllows(_scrolllerGate, target))", host);
        // A rebuilt engine already names its Source: its held page is loaded with Navigate.
        Assert.Contains("try { _web!.Navigate(src!); }", host);
        var gate = File.ReadAllText(Path.Combine(RepoRoot(), "CCP.Avalonia", "Platform", "ScrolllerWebHostGate.cs"));
        Assert.Contains("web.AdapterDestroyed += (_, _) => OnUi(() => { generation++; newEngine(); });", gate);
        // WPF SpiralEmbedView.cs:178-190: native settings land before first navigation is released.
        Assert.Contains("configureWindows?.Invoke(core); ready(AttachWebView2(core));", gate);
    }

    [Fact]
    public async System.Threading.Tasks.Task A_rebuilt_engine_is_held_again_and_an_old_engines_report_is_dropped() =>
        await CCP.Avalonia.Testing.AvaloniaTestDispatcher.RunAsync(() =>
        {
            var web = new WebHost();
            var first = web.NewScrolllerEngine();
            Assert.Null(web.ScrolllerGate);
            first(true);
            Assert.True(web.ScrolllerGate);
            var second = web.NewScrolllerEngine();   // unloaded and loaded again: a new native control
            Assert.Null(web.ScrolllerGate);
            first(true);                             // the old engine's late report
            Assert.Null(web.ScrolllerGate);
            second(false);
            Assert.False(web.ScrolllerGate);
            return System.Threading.Tasks.Task.CompletedTask;
        });

    [Theory]
    [InlineData("http://127.0.0.1:5/a/", "http://127.0.0.1:5/a/", false, true)]   // rebuilt engine, nothing loaded yet
    [InlineData("http://127.0.0.1:5/a/", "http://127.0.0.1:5/a/", true, false)]   // its page already started
    [InlineData(null, "http://127.0.0.1:5/a/", false, false)]                     // first engine: Source is assigned
    [InlineData("http://127.0.0.1:5/b/", "http://127.0.0.1:5/a/", false, false)]  // a different page: Source is assigned
    [InlineData("http://127.0.0.1:5/a/", null, false, false)]
    public void A_rebuilt_engine_reloads_its_held_page_with_navigate(string? engine, string? source, bool loaded, bool reload)
        => Assert.Equal(reload, WebHost.ReloadAfterGate(engine == null ? null : new Uri(engine), source == null ? null : new Uri(source), loaded));

    [Theory]
    [InlineData(true, true, false)]    // Windows, WPF's gate on the core: raw urls, the gate answers them
    [InlineData(null, true, true)]     // not reported yet: the relay
    [InlineData(false, true, true)]    // no gate: the relay
    [InlineData(true, false, true)]    // Linux: the engine blocks scrolller.com, always the relay
    [InlineData(null, false, true)]
    public void Windows_frames_keep_raw_urls_only_behind_the_gate(bool? gate, bool windows, bool relay)
        => Assert.Equal(relay, WebHost.FramesUseRelay(gate, windows));

    [Fact]
    public async System.Threading.Tasks.Task A_gated_windows_view_posts_the_raw_scrolller_url() =>
        await CCP.Avalonia.Testing.AvaloniaTestDispatcher.RunAsync(() =>
        {
            const string frame = "{\"url\":\"https://images.scrolller.com/a/b.mp4\"}";
            var page = new Uri("http://127.0.0.1:5555/fyp/index.html");
            var web = new WebHost();
            var engine = web.NewScrolllerEngine();
            Assert.Contains("/scrolller-media/", web.ScrolllerFrame(frame, page));   // held: the relay
            engine(true);
            var gated = web.ScrolllerFrame(frame, page);
            if (OperatingSystem.IsWindows()) Assert.Equal(frame, gated);
            else Assert.Contains("/scrolller-media/", gated);
            Assert.Equal(frame, web.ScrolllerFrame(frame, new Uri("https://example.test/")));   // a remote site never gets the relay
            return System.Threading.Tasks.Task.CompletedTask;
        });

    [Fact]
    public void Every_host_frame_goes_through_the_webhost_rule()
    {
        var root = RepoRoot();
        foreach (var f in new[] { Path.Combine("Views", "Games", "GameWindow.cs"), Path.Combine("Views", "Windows", "IntakeHostWindow.cs") })
        {
            var src = File.ReadAllText(Path.Combine(root, "CCP.Avalonia", f));
            Assert.Contains("Web.ScrolllerFrame(JsonConvert.SerializeObject(message), PageUrl)", src);
            Assert.DoesNotContain("ScrolllerRelay.RewriteJson(", src);
        }
    }

    [Fact]
    public void The_linux_hold_has_a_deadline()
    {
        var a = new List<bool>();
        var late = new ScrolllerWebHostGate.Deadline(a.Add);
        late.Expire(); late.Report(true);                 // too late but gated: it still goes through
        Assert.Equal(new[] { false, true }, a);
        var b = new List<bool>();
        var failed = new ScrolllerWebHostGate.Deadline(b.Add);
        failed.Expire(); failed.Report(false);            // a late failure says nothing new
        Assert.Equal(new[] { false }, b);
        var c = new List<bool>();
        var onTime = new ScrolllerWebHostGate.Deadline(c.Add);
        onTime.Report(true); onTime.Expire();
        Assert.Equal(new[] { true }, c);
        Assert.Equal(TimeSpan.FromSeconds(5), ScrolllerWebHostGate.LinuxGateWait);
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
