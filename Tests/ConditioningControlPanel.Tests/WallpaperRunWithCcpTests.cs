using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Studio > Wallpaper: when it is up, what panic does to it, and how the user's own desktop comes back.
/// </summary>
public class WallpaperRunWithCcpTests
{
    [Fact]
    public void Off_by_default_on_a_fresh_install_and_an_empty_settings_file()
    {
        Assert.False(new AppSettings().CollageRunWithCcp);
        Assert.False(JsonConvert.DeserializeObject<AppSettings>("{}")!.CollageRunWithCcp);
        Assert.False(new AppSettings().CollageWallpaperEnabled);
    }

    [Theory]
    //          wallpaper, runWithCcp, engine, expected
    [InlineData(false, false, false, false)]
    [InlineData(false, true, true, false)]    // Wallpaper off wins over everything
    [InlineData(true, false, true, true)]     // a session runs
    [InlineData(true, false, false, false)]   // idle, normal: no wallpaper
    [InlineData(true, true, false, true)]     // idle, Run with CCP: stays up
    [InlineData(true, true, true, true)]
    public void ShouldBeUp_follows_the_wallpaper_switch_the_engine_and_run_with_ccp(
        bool wallpaper, bool runWithCcp, bool engine, bool expected)
    {
        var s = new AppSettings { CollageWallpaperEnabled = wallpaper, CollageRunWithCcp = runWithCcp };
        Assert.Equal(expected, WallpaperService.ShouldBeUp(s, engine));
    }

    [Fact]
    public void ShouldBeUp_is_false_without_settings()
        => Assert.False(WallpaperService.ShouldBeUp(null, engineRunning: true));

    [Fact]
    public void A_cloud_restore_never_switches_it_on_or_brings_a_path_from_another_PC()
    {
        Assert.True(ProfileSyncService.IsExcludedFromBackup(nameof(AppSettings.CollageRunWithCcp)));
        Assert.True(ProfileSyncService.IsExcludedFromBackup(nameof(AppSettings.CollageOwnImagePath)));

        var current = new AppSettings { CollageRunWithCcp = false, CollageOwnImagePath = "here.jpg" };
        var restored = new AppSettings { CollageRunWithCcp = true, CollageOwnImagePath = "there.jpg" };
        ProfileSyncService.PreserveLocalOnlyFields(current, restored);

        Assert.False(restored.CollageRunWithCcp);
        Assert.Equal("here.jpg", restored.CollageOwnImagePath);
    }

    // ---- panic: hard rule 6, a stop-everything gesture is never more permissive than the panic key

    [Fact]
    public void Panic_takes_the_collage_down_before_the_takeover_restore()
    {
        // Every panic path (legacy ladder and override pass) ends in KillAllAudio. Deactivate refuses while
        // the collage runs, so StopCollage has to come first, "Run with CCP" or not.
        var app = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "App.xaml.cs"));
        int kill = app.IndexOf("public static void KillAllAudio()", StringComparison.Ordinal);
        int stop = app.IndexOf("Wallpaper?.StopCollage();", kill, StringComparison.Ordinal);
        int deactivate = app.IndexOf("Wallpaper?.Deactivate();", kill, StringComparison.Ordinal);
        Assert.True(kill >= 0 && stop > kill && deactivate > stop);
    }

    // ---- restore: the user's own wallpaper, never a copy, unless the copy is all that is left

    [Fact]
    public void Restore_prefers_the_users_own_file_over_the_frozen_copy()
    {
        var both = new HashSet<string> { "own.jpg", "frozen.jpg" };
        Assert.Equal("own.jpg", WallpaperService.RestoreTarget("own.jpg", both.Contains, "frozen.jpg"));
    }

    [Fact]
    public void Restore_falls_back_to_the_frozen_copy_when_the_source_is_gone()
    {
        var copy = new HashSet<string> { "frozen.jpg" };
        Assert.Equal("frozen.jpg", WallpaperService.RestoreTarget("own.jpg", copy.Contains, "frozen.jpg"));
        Assert.True(WallpaperService.IsRestorable("own.jpg", copy.Contains, "frozen.jpg"));
    }

    [Fact]
    public void Restore_clears_a_solid_colour_and_refuses_nothing_at_all()
    {
        var none = new HashSet<string>();
        Assert.Equal("", WallpaperService.RestoreTarget(WallpaperService.SolidColorMarker, none.Contains, "frozen.jpg"));
        Assert.True(WallpaperService.IsRestorable(WallpaperService.SolidColorMarker, none.Contains, "frozen.jpg"));
        Assert.False(WallpaperService.IsRestorable("own.jpg", none.Contains, "frozen.jpg"));
        Assert.False(WallpaperService.IsRestorable("", none.Contains, "frozen.jpg"));
        Assert.False(WallpaperService.IsRestorable(null, none.Contains, "frozen.jpg"));
    }

    [Theory]
    [InlineData(0, false)]   // a picture
    [InlineData(1, false)]   // a solid colour
    [InlineData(2, true)]    // a slideshow would stop
    [InlineData(3, true)]    // Spotlight would stop
    [InlineData(-1, false)]  // unknown: same picture everywhere
    public void A_write_is_refused_where_it_would_lose_the_desktop(int backgroundType, bool lost)
        => Assert.Equal(lost, WallpaperService.WouldLoseDesktop(backgroundType, new[] { "a.jpg", "A.JPG" }));

    [Fact]
    public void A_different_picture_per_monitor_counts_as_losing_the_desktop()
        => Assert.True(WallpaperService.WouldLoseDesktop(0, new[] { "a.jpg", "b.jpg" }));

    // ---- ccp-bugs #1584: a Windows 10 slideshow ended on a white desktop after a session

    [Theory]
    [InlineData(-1, 3, "slideshow")]   // no BackgroundType (some Windows 10 desktops), GetStatus says slideshow
    [InlineData(0, 2, "slideshow")]
    [InlineData(2, -1, "slideshow")]
    [InlineData(3, 1, "Spotlight")]
    [InlineData(-1, 1, null)]          // enabled, no slideshow: a plain picture
    [InlineData(0, -1, null)]          // GetStatus failed: BackgroundType alone decides
    public void A_slideshow_is_caught_by_either_reading(int backgroundType, int slideshowStatus, string? why)
        => Assert.Equal(why, WallpaperService.DesktopLossReason(backgroundType, slideshowStatus, new[] { "a.jpg" }));

    private const string Cache = @"C:\Users\u\AppData\Roaming\Microsoft\Windows\Themes\TranscodedWallpaper";

    [Fact]
    public void Windows_own_cache_is_recognised_whatever_the_case()
    {
        Assert.True(WallpaperService.IsTranscodedCache(Cache, Cache));
        Assert.True(WallpaperService.IsTranscodedCache(Cache.ToLowerInvariant(), Cache));
        Assert.False(WallpaperService.IsTranscodedCache(@"C:\pics\a.jpg", Cache));
        Assert.False(WallpaperService.IsTranscodedCache(null, Cache));
    }

    [Theory]
    [InlineData(1, @"C:\pics\a.jpg", "")]              // solid colour: clear
    [InlineData(0, @"C:\pics\a.jpg", @"C:\pics\a.jpg")] // picture: re-set it
    [InlineData(0, Cache, null)]                        // picture via Photos / a browser: never re-set the cache from itself
    [InlineData(2, @"C:\pics\a.jpg", null)]             // slideshow: redraw only
    [InlineData(-1, Cache, null)]                       // no BackgroundType (Windows 10 slideshow): redraw only
    public void Dynamics_stop_repaint_never_re_sets_windows_own_cache(int backgroundType, string path, string? target)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\pics\a.jpg", Cache };
        Assert.Equal(target, DynamicWallpaperService.RepaintTarget(backgroundType, path, files.Contains,
            p => WallpaperService.IsTranscodedCache(p, Cache)));
    }

    [Fact]
    public void A_cloud_restore_keeps_this_pcs_wallpaper_breadcrumb_and_fit()
    {
        var current = new AppSettings { WallpaperOriginalPath = @"C:\pics\mine.jpg", WallpaperOriginalStyle = "22|0" };
        var restored = new AppSettings { WallpaperOriginalPath = @"D:\other-pc.jpg", WallpaperOriginalStyle = "2|0" };
        ConditioningControlPanel.Services.ProfileSyncService.PreserveLocalOnlyFields(current, restored);
        Assert.Equal(@"C:\pics\mine.jpg", restored.WallpaperOriginalPath);
        Assert.Equal("22|0", restored.WallpaperOriginalStyle);
    }

    [Fact]
    public void The_cache_is_never_restored_as_itself_only_from_its_frozen_copy()
    {
        // Windows holds OUR picture in that file by the time of the restore; setting it from itself is the white desktop.
        var both = new HashSet<string> { Cache, "frozen.jpg" };
        Assert.Equal("frozen.jpg", WallpaperService.RestoreTarget(Cache, both.Contains, "frozen.jpg", Cache));
        Assert.True(WallpaperService.IsRestorable(Cache, both.Contains, "frozen.jpg", Cache));

        var cacheOnly = new HashSet<string> { Cache };
        Assert.False(WallpaperService.IsRestorable(Cache, cacheOnly.Contains, "frozen.jpg", Cache));
    }

    // ---- ccp-bugs #1544: a fit for the pictures CCP writes; the user's own comes back

    [Theory]
    [InlineData(WallpaperFit.Fill, "10|0")]
    [InlineData(WallpaperFit.Fit, "6|0")]
    [InlineData(WallpaperFit.Stretch, "2|0")]
    [InlineData(WallpaperFit.Span, "22|0")]
    [InlineData(WallpaperFit.Center, "0|0")]
    public void Each_fit_writes_the_windows_registry_pair(WallpaperFit fit, string packed)
        => Assert.Equal(packed, WallpaperService.FitStyle(fit));

    [Fact]
    public void The_default_fit_leaves_the_users_own_untouched()
    {
        Assert.Equal(WallpaperFit.Windows, new AppSettings().CollageWallpaperFit);
        Assert.Equal(WallpaperFit.Windows, JsonConvert.DeserializeObject<AppSettings>("{}")!.CollageWallpaperFit);
        Assert.Null(WallpaperService.FitStyle(WallpaperFit.Windows));
        Assert.Equal(WallpaperFit.Windows, JsonConvert.DeserializeObject<AppSettings>("{\"CollageWallpaperFit\":99}")!.CollageWallpaperFit);
    }

    [Fact]
    public void The_users_fit_round_trips_through_the_breadcrumb()
    {
        Assert.True(WallpaperService.TryUnpackStyle(WallpaperService.PackStyle("0", "1"), out var style, out var tile));
        Assert.Equal(("0", "1"), (style, tile));
        Assert.Equal("Tile", WallpaperService.WallpaperStyleName(style, tile));
        Assert.False(WallpaperService.TryUnpackStyle("", out _, out _));
        Assert.False(WallpaperService.TryUnpackStyle(null, out _, out _));
    }

    [Fact]
    public void A_fit_write_keeps_its_place_in_the_wallpaper_queue()
    {
        var order = new List<string>();
        var queue = new WallpaperSetQueue(p => { lock (order) order.Add(p); return true; });
        queue.Enqueue("ours.jpg");
        queue.Enqueue(() => { lock (order) order.Add("fit+own.jpg"); return true; });
        queue.Enqueue("later.jpg");
        Assert.True(queue.Flush(TimeSpan.FromSeconds(5)));
        Assert.Equal(new[] { "ours.jpg", "fit+own.jpg", "later.jpg" }, order);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "App.xaml.cs")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
