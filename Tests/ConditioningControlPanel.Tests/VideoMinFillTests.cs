using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Video;
using Newtonsoft.Json;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// "Fill the minimum with other videos": off by default and for every older settings file; on, a
/// clip shorter than the minimum is followed by other random clips, never the same one twice in a
/// row while the pool has another, until the minimum is reached. A pool of one loops instead.
/// </summary>
public class VideoMinFillTests
{
    private const int Minimum = 60;

    private static List<(string Path, long Ms)> Pool(params (string, long)[] clips) => clips.ToList();

    [Fact]
    public void Default_IsOff()
        => Assert.False(new AppSettings().MandatoryVideoFillShortWithOthers);

    [Fact]
    public void AnOlderSettingsFile_LoadsWithTheFillOff_AndTheLoopUnchanged()
    {
        var s = JsonConvert.DeserializeObject<AppSettings>(
            "{\"VideoMinDurationSeconds\":60,\"MandatoryVideoLoopShortClips\":true}")!;
        Assert.False(s.MandatoryVideoFillShortWithOthers);
        Assert.True(s.MandatoryVideoLoopShortClips);
    }

    [Fact]
    public void TheSetting_SurvivesASaveAndLoad_AndAPreset()
    {
        var json = JsonConvert.SerializeObject(new AppSettings { MandatoryVideoFillShortWithOthers = true });
        Assert.True(JsonConvert.DeserializeObject<AppSettings>(json)!.MandatoryVideoFillShortWithOthers);

        var preset = Preset.FromSettings(new AppSettings { MandatoryVideoFillShortWithOthers = true }, "p");
        Assert.True(preset.MandatoryVideoFillShortWithOthers);
        var applied = new AppSettings();
        preset.ApplyTo(applied);
        Assert.True(applied.MandatoryVideoFillShortWithOthers);
        Assert.False(applied.MandatoryVideoLoopShortClips);
    }

    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    public void SurfacesThatKeepTodaysBehaviour_NeverFill(bool floating, bool segment, bool enhanced, bool credited)
    {
        Assert.False(VideoMinFill.Applies(true, Minimum, floating, segment, enhanced, credited));
        Assert.True(VideoMinFill.Applies(true, Minimum));
        Assert.False(VideoMinFill.Applies(false, Minimum));
        Assert.False(VideoMinFill.Applies(true, 0));
    }

    [Fact]
    public void AShortClip_IsFollowedByOtherClipsUntilTheMinimum()
    {
        var plan = VideoMinFill.Plan("a", 20_000, Minimum,
            Pool(("b", 15_000), ("c", 15_000), ("d", 15_000)), new Random(1));
        // 20 + 15 + 15 + 15 = 65 s: three other clips, all different, none the first.
        Assert.Equal(3, plan.Count);
        Assert.Equal(3, plan.Select(p => p.Path).Distinct().Count());
        Assert.DoesNotContain(plan, p => p.Path == "a");
        Assert.True(20_000 + plan.Sum(p => p.Ms) >= Minimum * 1000L);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public void NeverTheSameClipTwiceInARow(int poolSize)
    {
        var pool = Enumerable.Range(0, poolSize).Select(i => ($"c{i}", 3_000L)).ToList();
        for (int seed = 0; seed < 200; seed++)
        {
            var plan = VideoMinFill.Plan("c0", 3_000, Minimum, pool, new Random(seed));
            Assert.NotEmpty(plan);
            string previous = "c0";
            foreach (var p in plan)
            {
                Assert.NotEqual(previous, p.Path);
                previous = p.Path;
            }
        }
    }

    [Fact]
    public void EveryClipPlays_BeforeAnyRepeats()
    {
        var pool = Enumerable.Range(0, 6).Select(i => ($"c{i}", 2_000L)).ToList();
        for (int seed = 0; seed < 50; seed++)
        {
            var plan = VideoMinFill.Plan("first", 2_000, Minimum, pool, new Random(seed));
            // Six others plus the first clip itself (it may come back, never straight away).
            Assert.Equal(7, plan.Take(7).Select(p => p.Path).Distinct().Count());
        }
    }

    [Fact]
    public void APoolOfTwo_Alternates()
    {
        var plan = VideoMinFill.Plan("a", 10_000, Minimum, Pool(("a", 10_000), ("b", 10_000)), new Random(3));
        Assert.Equal(new[] { "b", "a", "b", "a", "b" }, plan.Select(p => p.Path).ToArray());
    }

    [Fact]
    public void NoOtherClip_GivesAnEmptyPlan_SoTheClipLoops()
    {
        Assert.Empty(VideoMinFill.Plan("a", 20_000, Minimum, Pool(), new Random(1)));
        Assert.Empty(VideoMinFill.Plan("a", 20_000, Minimum, Pool(("a", 20_000)), new Random(1)));
        Assert.Empty(VideoMinFill.Plan("a", 20_000, Minimum, Pool(("A", 20_000)), new Random(1)));   // same file, other case
    }

    [Fact]
    public void ClipsWithNoKnownLength_AreLeftOut()
    {
        var plan = VideoMinFill.Plan("a", 20_000, Minimum, Pool(("b", 0), ("c", -1), ("d", 50_000)), new Random(1));
        Assert.Equal(new[] { "d" }, plan.Select(p => p.Path).ToArray());
    }

    [Theory]
    [InlineData(60_000)]
    [InlineData(90_000)]
    public void AClipThatReachesTheMinimum_GetsNoFill(long firstMs)
        => Assert.Empty(VideoMinFill.Plan("a", firstMs, Minimum, Pool(("b", 10_000)), new Random(1)));

    [Fact]
    public void AnUnknownFirstLength_PlansForTheWholeMinimum()
    {
        var plan = VideoMinFill.Plan("a", 0, Minimum, Pool(("b", 20_000), ("c", 20_000)), new Random(1));
        Assert.True(plan.Sum(p => p.Ms) >= Minimum * 1000L);
        Assert.DoesNotContain(plan, p => p.Path == "a");   // its length is unknown, so it never comes back
    }

    [Fact]
    public void AVeryShortPool_IsCapped()
    {
        var plan = VideoMinFill.Plan("a", 100, 600, Pool(("b", 100), ("c", 100)), new Random(1));
        Assert.Equal(VideoMinFill.MaxClips, plan.Count);
    }

    [Fact]
    public void TheSafetyTimer_AllowsForEveryPlannedClip()
    {
        var plan = Pool(("b", 15_000), ("c", 25_500));
        Assert.Equal(60.5, VideoMinFill.SafetySeconds(20.0, plan), 3);
        Assert.Equal(20.0, VideoMinFill.SafetySeconds(20.0, Pool()), 3);
    }

    [Fact]
    public void ThePageRule_StopsAtTheMinimum_OrWhenThePlanRunsOut()
    {
        // player.js 'ended': next planned clip while short of the minimum and one is left.
        long until = VideoMinLoop.PageLoopUntilMs(true, Minimum);
        var plan = VideoMinFill.Plan("a", 20_000, Minimum,
            Pool(("b", 30_000), ("c", 30_000), ("d", 30_000)), new Random(2));
        long played = 20_000;
        int next = 0;
        while (VideoMinFill.NextAfterClip(until, played, next, plan.Count)) { played += plan[next].Ms; next++; }
        Assert.Equal(plan.Count, next);          // the host planned exactly what the page plays
        Assert.True(played >= until);
        Assert.False(VideoMinFill.NextAfterClip(until, until, 0, 5));
        Assert.False(VideoMinFill.NextAfterClip(until, 1_000, 2, 2));
        Assert.False(VideoMinFill.NextAfterClip(0, 1_000, 0, 2));
    }
}
