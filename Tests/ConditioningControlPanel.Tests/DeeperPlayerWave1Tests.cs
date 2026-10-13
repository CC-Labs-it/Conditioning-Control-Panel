using System.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Deeper;
using ConditioningControlPanel.Views.Deeper;
using Newtonsoft.Json;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Deeper player wave 1: the volume level must survive with no output device
/// (it used to be a no-op setter + a hard-coded 80 getter), the persisted
/// setting clamps, and a remembered window position is only reused when
/// enough of the window lands on the virtual desktop.
/// </summary>
public class DeeperPlayerWave1Tests
{
    [Fact]
    public void Volume_IsCachedBeforeAnyOutputExists()
    {
        using var player = new EnhancementAudioPlayer();
        Assert.Equal(EnhancementAudioPlayer.DefaultVolume, player.Volume);
        player.Volume = 35;
        Assert.Equal(35, player.Volume);
    }

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(0, 0)]
    [InlineData(100, 100)]
    [InlineData(250, 100)]
    public void Volume_Clamps(int set, int expected)
    {
        using var player = new EnhancementAudioPlayer();
        player.Volume = set;
        Assert.Equal(expected, player.Volume);
        Assert.Equal(expected, EnhancementAudioPlayer.ClampVolume(set));
    }

    // Desk run 2026-10-13: an MP3 stops 1055 bytes short of the reported length, so the old
    // 1024-byte margin never saw the end and the playlist never moved to track two.
    [Theory]
    [InlineData(false, 3530976L, 3532031L, 88200, true)]   // the measured MP3: clean stop, 1055 short
    [InlineData(false, 100L, 3532031L, 88200, true)]       // a clean stop is the end wherever it lands
    [InlineData(true, 3530976L, 3532031L, 88200, true)]    // error inside the last half second
    [InlineData(true, 1000000L, 3532031L, 88200, false)]   // device pulled mid-track
    [InlineData(false, -1L, 0L, 0, false)]                 // nothing loaded
    public void NaturalEnd_IsACleanStop_NotAByteCount(bool hadError, long position, long length, int bytesPerSecond, bool expected)
    {
        Assert.Equal(expected, EnhancementAudioPlayer.IsNaturalEnd(hadError, position, length, bytesPerSecond));
    }

    [Fact]
    public void Volume_SurvivesStop()
    {
        using var player = new EnhancementAudioPlayer();
        player.Volume = 42;
        player.Stop();
        Assert.Equal(42, player.Volume);
    }

    [Fact]
    public void Settings_DeeperPlayerVolume_DefaultsAndClamps()
    {
        var fresh = new AppSettings();
        Assert.Equal(80, fresh.DeeperPlayerVolume);
        Assert.Equal(0, fresh.DeeperPlayerWindowWidth);

        fresh.DeeperPlayerVolume = 140;
        Assert.Equal(100, fresh.DeeperPlayerVolume);
        fresh.DeeperPlayerVolume = -5;
        Assert.Equal(0, fresh.DeeperPlayerVolume);

        var loaded = JsonConvert.DeserializeObject<AppSettings>("{\"DeeperPlayerVolume\": 55}")!;
        Assert.Equal(55, loaded.DeeperPlayerVolume);
    }

    [Fact]
    public void WindowRect_OnScreen_IsUsable()
    {
        var screen = new Rect(0, 0, 1920, 1080);
        Assert.True(EnhancementPlayerWindow.IsRectUsableOnScreen(new Rect(100, 100, 900, 768), screen));
        // Partly off the right edge but still grabbable.
        Assert.True(EnhancementPlayerWindow.IsRectUsableOnScreen(new Rect(1700, 100, 900, 768), screen));
    }

    [Fact]
    public void WindowRect_OffScreen_IsNotUsable()
    {
        var screen = new Rect(0, 0, 1920, 1080);
        // Saved on a monitor that is no longer there.
        Assert.False(EnhancementPlayerWindow.IsRectUsableOnScreen(new Rect(2500, 100, 900, 768), screen));
        Assert.False(EnhancementPlayerWindow.IsRectUsableOnScreen(new Rect(-880, 100, 900, 768), screen));
        Assert.False(EnhancementPlayerWindow.IsRectUsableOnScreen(new Rect(100, 1050, 900, 768), screen));
        Assert.False(EnhancementPlayerWindow.IsRectUsableOnScreen(new Rect(double.NaN, 0, 900, 768), screen));
        Assert.False(EnhancementPlayerWindow.IsRectUsableOnScreen(new Rect(0, 0, 900, 768), Rect.Empty));
    }
}
