using System;
using System.IO;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// An online wallpaper never stays set in Windows once it must go (day limit, off switch, or the
/// user switching wallpaper off) even when the user's own original was deleted meanwhile: a
/// neutral picture goes up instead of leaving the remote one on the desktop.
/// </summary>
public sealed class WallpaperRemoteCoverTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ccp-wp-cover-" + Guid.NewGuid().ToString("N"));

    public WallpaperRemoteCoverTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string RemoteTemp() => Path.Combine(_dir, "ccp_temp_remote_" + Guid.NewGuid().ToString("N") + ".jpg");

    [Fact]
    public void A_missing_original_with_an_online_picture_up_is_covered()
    {
        Assert.True(WallpaperService.MustCoverRemote(Path.Combine(_dir, "gone.jpg"), RemoteTemp()));
        Assert.True(WallpaperService.MustCoverRemote(null, RemoteTemp()));
    }

    [Fact]
    public void A_usable_original_is_restored_as_before()
    {
        var own = Path.Combine(_dir, "mine.jpg");
        File.WriteAllBytes(own, new byte[] { 1 });
        Assert.False(WallpaperService.MustCoverRemote(own, RemoteTemp()));
    }

    [Fact]
    public void A_local_picture_on_the_desktop_is_left_as_it_is()
    {
        Assert.False(WallpaperService.MustCoverRemote(null, Path.Combine(_dir, "holiday.jpg")));
        Assert.False(WallpaperService.MustCoverRemote(null, null));
    }

    [Fact]
    public void The_neutral_picture_is_Windows_default_when_there()
    {
        var def = Path.Combine(_dir, "img0.jpg");
        File.WriteAllBytes(def, new byte[] { 1 });
        Assert.Equal(def, WallpaperService.NeutralWallpaperPath(def, _dir));
    }

    [Fact]
    public void Else_a_plain_bitmap_is_written_once()
    {
        var path = WallpaperService.NeutralWallpaperPath(Path.Combine(_dir, "no-such.jpg"), _dir);
        Assert.NotNull(path);
        var bytes = File.ReadAllBytes(path!);
        Assert.Equal((byte)'B', bytes[0]);
        Assert.Equal((byte)'M', bytes[1]);
        Assert.Equal(54 + 16 * 16 * 3, bytes.Length);
        Assert.Equal(bytes.Length, BitConverter.ToInt32(bytes, 2));
        Assert.Equal(path, WallpaperService.NeutralWallpaperPath(null, _dir));
    }

    [Fact]
    public void Neither_means_none()
    {
        Assert.Null(WallpaperService.NeutralWallpaperPath(null, null));
    }

    [Theory]
    [InlineData("scrolller", true, true)]
    [InlineData("mixed", true, true)]
    [InlineData("scrolller", false, false)]
    [InlineData("local", true, false)]
    [InlineData(null, true, false)]
    public void The_credit_shows_only_while_online_media_may_reach_the_wallpaper(string? source, bool consent, bool shown)
    {
        Assert.Equal(shown, WallpaperService.RemoteMediaEnabled(source, consent));
    }

    [Fact]
    public void Bitmap_rows_are_padded_to_four_bytes()
    {
        var bmp = WallpaperService.SolidBmp(5, 2, 1, 2, 3);
        Assert.Equal(54 + 16 * 2, bmp.Length);   // 5 * 3 = 15, padded to 16
        Assert.Equal(3, bmp[54]);                 // BGR order
        Assert.Equal(1, bmp[56]);
    }
}
