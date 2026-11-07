using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services;
using Xunit;
using static ConditioningControlPanel.Services.VideoWedgeLadder;

namespace CCP.Core.Tests;

/// <summary>
/// The rules moved out of WPF VideoService (video-core-d): clip selection, the post-poisoning
/// cooldown, the retire verdict, the UI-wedge rescue ladder and the strict close veto. The trigger
/// guard, grace timers and vout-loss machine keep their moved suites (VideoTriggerGuardTests,
/// VideoGracePauseTests, VoutMidPlayTests).
/// </summary>
public class VideoServiceRulesTests
{
    private static long Ms(long ms) => ms * TimeSpan.TicksPerMillisecond;

    // ---- selection ----

    [Theory]
    [InlineData("https://cdn.example/a.mp4", true)]
    [InlineData("HTTP://cdn.example/a.mp4", true)]
    [InlineData(@"C:\videos\a.mp4", false)]
    [InlineData("/home/u/videos/a.mp4", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void RemotePath_IsAnHttpUrl(string? path, bool remote)
        => Assert.Equal(remote, VideoSelectionRules.IsRemoteMediaPath(path));

    [Fact]
    public void Remote_EmptyLibraryOrOnline_AlwaysAsksTheBuffer()
    {
        var rng = new Random(1);
        Assert.True(VideoSelectionRules.WantsRemote(localEmpty: true, "local", 0, rng));
        Assert.True(VideoSelectionRules.WantsRemote(localEmpty: false, "Online", 0, rng));
    }

    [Fact]
    public void Remote_Mixed_FollowsTheRatio()
    {
        var rng = new Random(7);
        Assert.All(Enumerable.Range(0, 50), _ => Assert.True(VideoSelectionRules.WantsRemote(false, "mixed", 100, rng)));
        Assert.All(Enumerable.Range(0, 50), _ => Assert.False(VideoSelectionRules.WantsRemote(false, "mixed", 0, rng)));
    }

    [Fact]
    public void Remote_NotMixed_TakesNoRoll()
    {
        // WPF rolls only for "mixed": any other source leaves the shared Random untouched.
        var used = new Random(3);
        Assert.False(VideoSelectionRules.WantsRemote(false, "local", 100, used));
        Assert.Equal(new Random(3).Next(), used.Next());
    }

    [Fact]
    public void Shuffle_IsAPermutation()
    {
        var list = Enumerable.Range(0, 40).ToList();
        VideoSelectionRules.Shuffle(list, new Random(5));
        Assert.Equal(Enumerable.Range(0, 40), list.OrderBy(x => x));
        Assert.NotEqual(Enumerable.Range(0, 40), list);
    }

    [Fact]
    public void Funnel_NamesTheBiggestDrop()
    {
        Assert.Equal("the videos folder does not exist", VideoSelectionRules.RefillBiggestDrop(true, 0, 0, 0, 0, 0, 0, 0));
        Assert.Equal("the folder is empty", VideoSelectionRules.RefillBiggestDrop(false, 0, 0, 0, 0, 0, 0, 0));
        Assert.Equal("nothing was dropped", VideoSelectionRules.RefillBiggestDrop(false, 5, 5, 5, 5, 5, 0, 0));
        Assert.StartsWith("extension filter (7 file(s); supported: .mp4",
            VideoSelectionRules.RefillBiggestDrop(false, 10, 3, 3, 3, 3, 0, 0));
        Assert.Equal("duration filter (4 file(s); min 30s max 120s)",
            VideoSelectionRules.RefillBiggestDrop(false, 10, 10, 9, 8, 4, 30, 120));
        // A tie goes to the earlier step of the walk.
        Assert.Equal("path/name security checks (2 file(s))",
            VideoSelectionRules.RefillBiggestDrop(false, 10, 10, 8, 6, 6, 0, 0));
    }

    [Fact]
    public void LengthFilter_KeepsCacheMisses_AsWpfRefillDoes()
    {
        var lengths = new Dictionary<string, double> { ["short"] = 5, ["ok"] = 60, ["long"] = 900 };
        var kept = MandatoryVideoScheduler.KeepByLength(new[] { "short", "ok", "long", "unparsed" }, 10, 600,
            f => lengths.TryGetValue(f, out var d) ? d : null);
        Assert.Equal(new[] { "ok", "long", "unparsed" }, kept);
    }

    // ---- cooldown ----

    [Fact]
    public void PoisonCooldown_NeverArmed_IsClear()
        => Assert.Equal(0, VideoCooldownRules.PoisonCooldownRemainingMs(0, Ms(123_456)));

    [Fact]
    public void PoisonCooldown_CountsDownToZero()
    {
        Assert.Equal(60000, VideoCooldownRules.PoisonCooldownRemainingMs(Ms(1000), Ms(1000)));
        Assert.Equal(15000, VideoCooldownRules.PoisonCooldownRemainingMs(Ms(1000), Ms(46000)));
        Assert.Equal(0, VideoCooldownRules.PoisonCooldownRemainingMs(Ms(1000), Ms(61000)));
        Assert.Equal(0, VideoCooldownRules.PoisonCooldownRemainingMs(Ms(1000), Ms(999_000)));
    }

    // ---- retire verdict ----

    [Fact]
    public void Retire_GoesAheadOnlyForTheLiveSharedInstanceUnderTheCap()
    {
        Assert.Equal(RetireVerdict.AlreadyRetired, DecideRetire(hasShared: false, suspectIsShared: false, 0));
        Assert.Equal(RetireVerdict.NotShared, DecideRetire(hasShared: true, suspectIsShared: false, 0));
        Assert.Equal(RetireVerdict.Retire, DecideRetire(hasShared: true, suspectIsShared: true, MaxRetiresPerSession - 1));
        Assert.Equal(RetireVerdict.CapReached, DecideRetire(hasShared: true, suspectIsShared: true, MaxRetiresPerSession));
    }

    // ---- wedge ladder ----

    [Fact]
    public void Ladder_UnderTheStall_DoesNothing()
        => Assert.Equal(WedgeAction.None, NextRung(StallMs - 1, 0, rescueAllowed: true, retireRung: true));

    [Fact]
    public void Ladder_Mandatory_ClimbsStopRetireHatch()
    {
        Assert.Equal(WedgeAction.StopPlayers, NextRung(StallMs, 0, true, true));
        Assert.Equal(WedgeAction.None, NextRung(Rung2Ms - 1, 1, true, true));
        Assert.Equal(WedgeAction.RetireInstance, NextRung(Rung2Ms, 1, true, true));
        // A stall that jumped straight past rung 3 still retires first; the hatch is the next tick.
        Assert.Equal(WedgeAction.RetireInstance, NextRung(Rung3Ms + 5000, 1, true, true));
        Assert.Equal(WedgeAction.None, NextRung(Rung3Ms - 1, 2, true, true));
        Assert.Equal(WedgeAction.EscapeHatch, NextRung(Rung3Ms, 2, true, true));
        Assert.Equal(WedgeAction.None, NextRung(Rung3Ms * 10, 3, true, true));
    }

    [Fact]
    public void Ladder_Leased_SkipsTheRetireRung()
    {
        Assert.Equal(WedgeAction.StopPlayers, NextRung(StallMs, 0, true, retireRung: false));
        Assert.Equal(WedgeAction.None, NextRung(Rung2Ms, 1, true, retireRung: false));
        Assert.Equal(WedgeAction.EscapeHatch, NextRung(Rung3Ms, 1, true, retireRung: false));
    }

    [Fact]
    public void Ladder_PreRollOrTeardown_OnlyEverOffersTheHatch()
    {
        // #750-#753: no native rescue while the UI thread may be walking into EnsureLibVLCInitialized.
        Assert.Equal(WedgeAction.None, NextRung(StallMs, 0, rescueAllowed: false, retireRung: true));
        Assert.Equal(WedgeAction.None, NextRung(Rung2Ms, 0, rescueAllowed: false, retireRung: true));
        Assert.Equal(WedgeAction.EscapeHatch, NextRung(Rung3Ms, 0, rescueAllowed: false, retireRung: true));
        Assert.Equal(WedgeAction.EscapeHatch, NextRung(Rung3Ms, 1, rescueAllowed: false, retireRung: true));
    }

    // ---- strict lock ----

    [Theory]
    [InlineData(true, false, false, true)]    // playing: Alt+F4 refused
    [InlineData(false, false, false, false)]  // nothing playing: a stale strict window must close
    [InlineData(true, true, false, false)]    // a real teardown is never vetoed
    [InlineData(true, false, true, false)]    // #765: the app wedged, the lock stands down
    public void StrictCloseVeto(bool playing, bool cleaning, bool wedged, bool vetoed)
        => Assert.Equal(vetoed, MandatoryVideoScheduler.StrictVetoesClose(playing, cleaning, wedged));
}
