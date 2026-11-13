using System;
using System.IO;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Studio > Wallpaper > "Save this wallpaper" (ccp-bugs #1542): a copy into Pictures\CCP Wallpapers that
/// never overwrites an earlier save and leaves the running wallpaper's file alone.
/// </summary>
public class WallpaperSaverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ccp-wpsave-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    private string Source(string name = "ccp_collage_abc.jpg", string body = "picture")
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, body);
        return path;
    }

    [Fact]
    public void Default_folder_is_CCP_Wallpapers_under_Pictures()
    {
        var folder = WallpaperSaver.DefaultFolder();
        Assert.Equal("CCP Wallpapers", Path.GetFileName(folder));
    }

    [Fact]
    public void Creates_the_folder_and_copies_with_a_dated_name_keeping_the_extension()
    {
        var src = Source("ccp_collage_abc.png");
        var folder = Path.Combine(_root, "out", "CCP Wallpapers");

        var saved = WallpaperSaver.SaveCopy(src, folder, new DateTime(2026, 11, 9, 14, 32, 5));

        Assert.Equal(Path.Combine(folder, "CCP wallpaper 2026-11-09 143205.png"), saved);
        Assert.Equal("picture", File.ReadAllText(saved));
        Assert.True(File.Exists(src));   // a copy: the running wallpaper's file is untouched
    }

    [Fact]
    public void A_second_save_in_the_same_second_never_overwrites_the_first()
    {
        var folder = Path.Combine(_root, "out");
        var when = new DateTime(2026, 11, 9, 14, 32, 5);

        var first = WallpaperSaver.SaveCopy(Source(body: "one"), folder, when);
        var second = WallpaperSaver.SaveCopy(Source(body: "two"), folder, when);
        var third = WallpaperSaver.SaveCopy(Source(body: "three"), folder, when);

        Assert.Equal("one", File.ReadAllText(first));
        Assert.EndsWith("143205 (2).jpg", second);
        Assert.EndsWith("143205 (3).jpg", third);
        Assert.Equal("two", File.ReadAllText(second));
    }

    [Fact]
    public void A_missing_source_throws_and_writes_nothing()
    {
        var folder = Path.Combine(_root, "out");
        Assert.Throws<FileNotFoundException>(() =>
            WallpaperSaver.SaveCopy(Path.Combine(_root, "gone.jpg"), folder, DateTime.Now));
        Assert.False(Directory.Exists(folder));
    }
}
