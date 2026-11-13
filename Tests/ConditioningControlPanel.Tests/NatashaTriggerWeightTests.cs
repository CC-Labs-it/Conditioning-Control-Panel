using System;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Chaster;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1566: a 1-10 weight for how much likelier a trigger bubble is to wear Natasha's red
/// than a plain one. 1 is today's one in ten for both; plain bubbles never move.
/// </summary>
public class NatashaTriggerWeightTests
{
    [Theory]
    [InlineData(false, 1, 1)]
    [InlineData(false, 7, 1)]
    [InlineData(true, 1, 1)]
    [InlineData(true, 5, 5)]
    [InlineData(true, 10, 10)]
    [InlineData(true, 0, 1)]
    [InlineData(true, 99, 10)]
    public void ChanceInTenFollowsTheWeightOnlyForTriggers(bool trigger, int weight, int expected)
        => Assert.Equal(expected, NatashasFavourite.ChanceInTen(trigger, weight));

    [Fact]
    public void WeightFiveMakesTriggersAboutFiveTimesAsRed()
    {
        var rng = new Random(20261105);
        const int n = 40_000;
        var plain = Enumerable.Range(0, n).Count(_ => NatashasFavourite.Roll(rng, false, 5)) / (double)n;
        var trigger = Enumerable.Range(0, n).Count(_ => NatashasFavourite.Roll(rng, true, 5)) / (double)n;
        Assert.InRange(plain, 0.085, 0.115);
        Assert.InRange(trigger, 0.47, 0.53);
    }

    [Fact]
    public void WeightOneIsTheOldOneInTenForTriggerBubbles()
    {
        var rng = new Random(7);
        var rate = Enumerable.Range(0, 40_000).Count(_ => NatashasFavourite.Roll(rng, true, 1)) / 40_000.0;
        Assert.InRange(rate, 0.085, 0.115);
    }

    [Fact]
    public void TheSettingDefaultsToOneAndClamps()
    {
        var s = new AppSettings();
        Assert.Equal(1, s.ChasterNatashaTriggerWeight);
        s.ChasterNatashaTriggerWeight = 0;
        Assert.Equal(1, s.ChasterNatashaTriggerWeight);
        s.ChasterNatashaTriggerWeight = 12;
        Assert.Equal(10, s.ChasterNatashaTriggerWeight);
    }

    [Fact]
    public void BrainDrainClipRateGoesToZero()
    {
        // ccp-bugs #1536: 0 = no clips, so the blur can run without the audio.
        var s = new AppSettings { BrainDrainIntensity = 0 };
        Assert.Equal(0, s.BrainDrainIntensity);
    }

    [Fact]
    public void AwarenessStartOnLaunchIsOffByDefault()
    {
        // ccp-bugs #1562: stored, off by default, so Awareness still starts off for everyone else.
        var s = new AppSettings();
        Assert.False(s.AwarenessStartOnLaunch);
        var back = Newtonsoft.Json.JsonConvert.DeserializeObject<AppSettings>(
            Newtonsoft.Json.JsonConvert.SerializeObject(new AppSettings { AwarenessStartOnLaunch = true }))!;
        Assert.True(back.AwarenessStartOnLaunch);
        Assert.False(back.KeywordTriggersEnabled); // the session flag itself is never persisted
    }
}
