using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// The Scrolller licence wiring on this head (WPF App.xaml.cs:2567-2576 + ScrolllerCopySweeper): the policy poll, the
/// device cache and the hourly sweep start at launch; the sweep and the off switch reach every copy this head holds
/// (temp copies, old WPF WebView2 caches); a DtRH copy is served no-store so the engine never keeps one.
/// </summary>
public sealed class ScrolllerHeadTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ccp-scr-head-").FullName;
    private readonly Func<System.Collections.Generic.IEnumerable<string?>> _dirs = ScrolllerHead.TempDirs;
    private readonly Func<System.Collections.Generic.IEnumerable<string>> _roots = LegacyWebViewCaches.RootsProvider;

    public void Dispose()
    {
        ScrolllerHead.TempDirs = _dirs;
        LegacyWebViewCaches.RootsProvider = _roots;
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Seeding_points_the_core_sweep_and_purge_at_this_head()
    {
        ScrolllerHead.SeedHooks();
        Assert.Equal((Action)ScrolllerHead.SweepAll, ScrolllerDiskCache.SweepAllCopies);
        Assert.Equal((Action<string>)ScrolllerHead.PurgeAll, ScrolllerDiskCache.PurgeAllCopies);
    }

    [Fact]
    public void Startup_wires_the_head_after_the_version_seed_and_flushes_on_exit()
    {
        var app = File.ReadAllText(Path.Combine(RepoRoot(), "CCP.Avalonia", "App.axaml.cs"));
        int version = app.IndexOf("CoreReleaseContent.AppVersionProvider =", StringComparison.Ordinal);
        int start = app.IndexOf("Platform.ScrolllerHead.Start();", StringComparison.Ordinal);
        Assert.True(version > 0 && start > version, "ScrolllerHead.Start runs after the honest user-agent's version seed");
        Assert.Contains("Platform.ScrolllerHead.Stop();", app);
        var head = File.ReadAllText(Path.Combine(RepoRoot(), "CCP.Avalonia", "Platform", "ScrolllerHead.cs"));
        foreach (var call in new[] { "ScrolllerPolicyService.Start", "ScrolllerDiskCache.WarmAsync", "ScrolllerDiskCache.StartSweeper" })
            Assert.Contains(call, head);
    }

    [Fact]
    public void The_off_switch_deletes_every_temp_copy_and_the_sweep_only_old_ones()
    {
        ScrolllerHead.TempDirs = () => new[] { _dir };
        LegacyWebViewCaches.RootsProvider = () => Array.Empty<string>();
        var old = Make("ccp_temp_remote_old.jpg", TimeSpan.FromHours(-30));
        var young = Make("ccp_temp_remote_young.jpg", TimeSpan.FromHours(-1));
        var ahead = Make("ccp_temp_remote_ahead.jpg", TimeSpan.FromHours(72));   // the clock went back
        var mine = Make("users-own.jpg", TimeSpan.FromHours(-30));                // never ours to delete

        ScrolllerHead.SweepAll();
        Assert.False(File.Exists(old));
        Assert.False(File.Exists(ahead));
        Assert.True(File.Exists(young));
        Assert.True(File.Exists(mine));

        ScrolllerHead.PurgeAll("test");
        Assert.False(File.Exists(young));
        Assert.True(File.Exists(mine));
    }

    [Fact]
    public void Old_wpf_webview2_caches_go_and_their_page_storage_stays()
    {
        var profile = Path.Combine(_dir, "browser_data_fyp", "EBWebView", "Default");
        Directory.CreateDirectory(Path.Combine(profile, "Cache", "Cache_Data"));
        File.WriteAllText(Path.Combine(profile, "Cache", "Cache_Data", "f_000001"), "clip");
        Directory.CreateDirectory(Path.Combine(profile, "Local Storage"));
        Directory.CreateDirectory(Path.Combine(_dir, "assets", "Cache"));   // not a WebView2 profile
        LegacyWebViewCaches.RootsProvider = () => new[] { _dir };

        Assert.Equal(1, LegacyWebViewCaches.SweepAll());
        Assert.False(Directory.Exists(Path.Combine(profile, "Cache")));
        Assert.True(Directory.Exists(Path.Combine(profile, "Local Storage")));
        Assert.True(Directory.Exists(Path.Combine(_dir, "assets", "Cache")));
        Assert.Equal(0, LegacyWebViewCaches.SweepAll());
    }

    [Fact]
    public void A_remote_copy_is_served_no_store()
    {
        Assert.Equal("no-store", WebAssetServer.CacheControlFor(Path.Combine(_dir, ".temp", "ccp_temp_remote_ab.jpg")));
        Assert.Equal("no-cache", WebAssetServer.CacheControlFor(Path.Combine(_dir, "images", "a.jpg")));
    }

    private string Make(string name, TimeSpan born)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, "x");
        File.SetCreationTimeUtc(path, DateTime.UtcNow + born);
        return path;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "CCP.Avalonia", "Platform"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
