using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Video;
using Newtonsoft.Json;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// "Loop short videos until the minimum time": off by default and for every older settings file;
/// on, a clip shorter than the minimum plays just enough whole passes to reach it and ends once.
/// </summary>
public class VideoMinLoopTests
{
    private const int Minimum = 60;

    [Fact]
    public void Default_IsOff()
        => Assert.False(new AppSettings().MandatoryVideoLoopShortClips);

    [Fact]
    public void AnOlderSettingsFile_LoadsWithTheLoopOff()
    {
        var s = JsonConvert.DeserializeObject<AppSettings>("{\"VideoMinDurationSeconds\":60,\"MandatoryVideosEnabled\":true}")!;
        Assert.Equal(60, s.VideoMinDurationSeconds);
        Assert.False(s.MandatoryVideoLoopShortClips);
    }

    [Fact]
    public void TheSetting_SurvivesASaveAndLoad()
    {
        var json = JsonConvert.SerializeObject(new AppSettings { MandatoryVideoLoopShortClips = true });
        Assert.True(JsonConvert.DeserializeObject<AppSettings>(json)!.MandatoryVideoLoopShortClips);
    }

    [Theory]
    [InlineData(25_000, 3)]   // shorter: 25 + 25 + 25 = 75 s, the first total past 60 s
    [InlineData(20_000, 3)]   // shorter, divides evenly: exactly 60 s, no extra pass
    [InlineData(59_999, 2)]
    [InlineData(60_000, 1)]   // equal: plays once
    [InlineData(90_000, 1)]   // longer: plays once
    public void Passes_ReachTheMinimumWithWholePasses(long clipMs, int expected)
    {
        var applies = VideoMinLoop.Applies(enabled: true, Minimum);
        Assert.Equal(expected, VideoMinLoop.Passes(applies, Minimum, clipMs));
        Assert.Equal(expected - 1, VideoMinLoop.RepeatCount(applies, Minimum, clipMs));
    }

    [Theory]
    [InlineData(25_000)]
    [InlineData(60_000)]
    [InlineData(90_000)]
    public void ToggleOff_PlaysOnceWhateverTheLength(long clipMs)
    {
        var applies = VideoMinLoop.Applies(enabled: false, Minimum);
        Assert.False(applies);
        Assert.Equal(1, VideoMinLoop.Passes(applies, Minimum, clipMs));
        Assert.Equal(0, VideoMinLoop.RepeatCount(applies, Minimum, clipMs));
        Assert.Equal(0, VideoMinLoop.PageLoopUntilMs(applies, Minimum));
    }

    [Fact]
    public void ToggleOff_TheMinimumStillSkipsShorterClips()
    {
        Assert.True(VideoMinLoop.FilterDropsShortClip(loopEnabled: false, Minimum, floating: false, clipSeconds: 25));
        Assert.False(VideoMinLoop.FilterDropsShortClip(loopEnabled: false, Minimum, floating: false, clipSeconds: 60));
        Assert.False(VideoMinLoop.FilterDropsShortClip(loopEnabled: false, 0, floating: false, clipSeconds: 1));
    }

    [Fact]
    public void ToggleOn_KeepsShorterClipsInThePool_ExceptFloatingWindows()
    {
        Assert.False(VideoMinLoop.FilterDropsShortClip(loopEnabled: true, Minimum, floating: false, clipSeconds: 25));
        // Floating windows do not loop, so they keep today's filter.
        Assert.True(VideoMinLoop.FilterDropsShortClip(loopEnabled: true, Minimum, floating: true, clipSeconds: 25));
    }

    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    public void SurfacesThatKeepTodaysBehaviour_NeverLoop(bool floating, bool segment, bool enhanced, bool credited)
        => Assert.False(VideoMinLoop.Applies(true, Minimum, floating, segment, enhanced, credited));

    [Fact]
    public void NoMinimum_OrUnknownLength_PlaysOnce()
    {
        Assert.False(VideoMinLoop.Applies(true, 0));
        Assert.Equal(1, VideoMinLoop.Passes(true, Minimum, 0));
        Assert.Equal(1, VideoMinLoop.Passes(true, Minimum, -1));
    }

    [Fact]
    public void AVeryShortClip_IsCappedAtWhatLibVlcAccepts()
        => Assert.Equal(VideoMinLoop.MaxPasses, VideoMinLoop.Passes(true, 600, 1));

    [Theory]
    [InlineData(25_000)]
    [InlineData(20_000)]
    [InlineData(59_999)]
    [InlineData(60_000)]
    [InlineData(90_000)]
    public void ThePageRule_PlaysTheSamePassesAsTheHostCounts(long clipMs)
    {
        // player.js 'ended': replay while the finished passes are short of the target.
        var until = VideoMinLoop.PageLoopUntilMs(true, Minimum);
        int passes = 1;
        long played = clipMs;
        while (VideoMinLoop.ReplayAfterPass(until, played)) { passes++; played += clipMs; }
        Assert.Equal(VideoMinLoop.Passes(true, Minimum, clipMs), passes);
    }

    [Fact]
    public void TheSafetyTimer_AllowsForEveryPass()
    {
        Assert.Equal(75.0, VideoMinLoop.SafetySeconds(25.0, 3));
        Assert.Equal(25.0, VideoMinLoop.SafetySeconds(25.0, 1));
        Assert.Equal(25.0, VideoMinLoop.SafetySeconds(25.0, 0));
    }
}
