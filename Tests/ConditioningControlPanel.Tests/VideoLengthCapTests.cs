using System.IO;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Browser;
using ConditioningControlPanel.Services.Video;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The player's longest-video limit holds on every surface, whatever the clip says about its own
/// length (ccp-bugs #1438: a companion takeover played a 25 minute web video past a 20 minute
/// limit; the browser takeover and the floating windows never read the setting).
/// </summary>
public class VideoLengthCapTests
{
    private const int TwentyMinutes = 1200;

    [Fact]
    public void Default_IsNoLimit_SoNothingChangesForAnyoneWhoNeverSetOne()
    {
        var s = new AppSettings();
        Assert.Equal(0, s.VideoMaxDurationSeconds);
        Assert.False(VideoLengthCap.IsSet(s.VideoMaxDurationSeconds));
        Assert.False(VideoLengthCap.Reached(s.VideoMaxDurationSeconds, long.MaxValue / 2));
        Assert.Null(VideoLengthCap.BrowserTakeoverSeconds(s.VideoMaxDurationSeconds, BrowserMediaService.MediaOwner.Autonomy));
    }

    [Theory]
    [InlineData(BrowserMediaService.MediaOwner.Autonomy)]
    [InlineData(BrowserMediaService.MediaOwner.Chaos)]
    [InlineData(BrowserMediaService.MediaOwner.Remote)]
    public void AVideoTheAppPutOnScreen_IsCutAtTheLimit(BrowserMediaService.MediaOwner owner)
        => Assert.Equal(TwentyMinutes, VideoLengthCap.BrowserTakeoverSeconds(TwentyMinutes, owner));

    [Theory]
    [InlineData(BrowserMediaService.MediaOwner.User)]
    [InlineData(BrowserMediaService.MediaOwner.AvatarLink)]
    public void AVideoThePlayerPicked_IsNeverCut(BrowserMediaService.MediaOwner owner)
        => Assert.Null(VideoLengthCap.BrowserTakeoverSeconds(TwentyMinutes, owner));

    [Fact]
    public void NoSession_NoCut()
        => Assert.Null(VideoLengthCap.BrowserTakeoverSeconds(TwentyMinutes, null));

    [Theory]
    [InlineData(0, false)]
    [InlineData(1_199_999, false)]
    [InlineData(1_200_000, true)]
    [InlineData(1_500_000, true)]
    public void AFloatingWindow_ClosesOnceItHasPlayedTheLimit(long playedMs, bool reached)
        => Assert.Equal(reached, VideoLengthCap.Reached(TwentyMinutes, playedMs));

    [Fact]
    public void NegativeSetting_ReadsAsNoLimit()
    {
        Assert.False(VideoLengthCap.IsSet(-5));
        Assert.False(VideoLengthCap.Reached(-5, 10_000));
    }

    // The cut must not hang on a reported length: the takeover arms it at first playback,
    // outside the "page gave a usable duration" branch, and each floating window checks it.
    [Fact]
    public void BothBrowserSurfaces_AskTheRule()
    {
        var media = File.ReadAllText(Source("Services", "Browser", "BrowserMediaService.cs"));
        var arm = media.IndexOf("VideoLengthCap.BrowserTakeoverSeconds", System.StringComparison.Ordinal);
        var usable = media.IndexOf("if (IsUsableDuration(durationSeconds))", System.StringComparison.Ordinal);
        Assert.True(arm > 0 && arm < usable, "the takeover cut is armed before, and apart from, the duration branch");
        Assert.Contains("\"max-video-length\"", media);

        var floating = File.ReadAllText(Source("Services", "Video", "VideoService.FloatingInstances.cs"));
        Assert.Contains("VideoLengthCap.Reached(", floating);
    }

    private static string Source(params string[] parts)
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Services")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(Path.Combine(dir!.FullName, "ConditioningControlPanel"), Path.Combine(parts));
    }
}
