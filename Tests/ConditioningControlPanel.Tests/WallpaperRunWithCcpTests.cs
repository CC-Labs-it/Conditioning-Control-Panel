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

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "App.xaml.cs")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
