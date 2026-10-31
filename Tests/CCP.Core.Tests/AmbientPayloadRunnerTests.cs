using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Core.Tests;

public sealed class AmbientPayloadRunnerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OverlappingHoldsKeepPeakUntilLastReleaseAndIgnoreStaleTimers(bool reverse)
    {
        var hold = new TimedOverlayHold();
        var strong = hold.Acquire(0.8); var weak = hold.Acquire(0.2);
        Assert.Equal(0.8, hold.Opacity);
        Assert.True((reverse ? strong : weak)());
        Assert.True(hold.IsActive); Assert.Equal(0.8, hold.Opacity);
        Assert.True((reverse ? weak : strong)()); Assert.False(hold.IsActive);
        var stale = hold.Acquire(1); hold.Clear(); var fresh = hold.Acquire(0.3);
        Assert.False(stale()); Assert.Equal(0.3, hold.Opacity);
        Assert.True(fresh()); Assert.False(hold.IsActive); Assert.False(fresh());
    }

    [Theory]
    [InlineData(ChaosMotion.RainDown)]
    [InlineData(ChaosMotion.RoamBounce)]
    [InlineData(ChaosMotion.SideDrift)]
    public void AmbientBoostScalesMotionVelocities(ChaosMotion motion)
    {
        var spec = AmbientBubblePayloads.Build("flash")!;
        double Travel(int boost)
        {
            var b = AmbientBubble.SpawnChaos(new Random(4), 0, 0, 0, 1920, 1080, 1, spec, motion, boost);
            var x = b.X; var y = b.Y; b.Step();
            return motion == ChaosMotion.SideDrift ? Math.Abs(b.X - x) : motion == ChaosMotion.RainDown ? b.Y - y : Math.Sqrt(Math.Pow(b.X - x, 2) + Math.Pow(b.Y - y, 2));
        }
        Assert.Equal(6 * Travel(0), Travel(500), 6);
    }

    [Fact]
    public void TriggerRollHonoursSwitchChanceCapAndRetiredIds()
    {
        var s = new AppSettings { BubbleTriggersEnabled = true, BubbleTriggerChance = 100, BubbleTriggerVariants = new() { "flash" } };
        var spec = AmbientBubblePayloads.Roll(s, new Random(1), 3)!;
        Assert.NotNull(spec);
        Assert.False(spec.IsLive);
        Assert.True(spec.Payload.Ambient);
        Assert.Equal(2.5, spec.Payload.DurationMult);
        Assert.Null(AmbientBubblePayloads.Roll(s, new Random(1), 4));
        s.BubbleTriggerChance = 0;
        Assert.Null(AmbientBubblePayloads.Roll(s, new Random(1), 0));
        s.BubbleTriggerChance = 100; s.BubbleTriggersEnabled = false;
        Assert.Null(AmbientBubblePayloads.Roll(s, new Random(1), 0));
        s.BubbleTriggersEnabled = true; s.BubbleTriggerVariants = new() { MagnetBubble.VariantId, "unknown" };
        Assert.Null(AmbientBubblePayloads.Roll(s, new Random(1), 0));
    }

    [Theory]
    [InlineData("pink")]
    [InlineData("spiral")]
    [InlineData("video")]
    [InlineData("htlink")]
    [InlineData("glitch")]
    public void AmbientVariantsAreHarmlessTimedTreats(string id)
    {
        var spec = AmbientBubblePayloads.Build(id)!;
        Assert.False(spec.IsLive);
        Assert.True(spec.Payload.Ambient);
        Assert.Equal(7000, spec.TreatLifeMs);
        Assert.Equal(id == "glitch" ? 1.2 : 2.5, spec.Payload.DurationMult);
    }

    [Theory]
    [InlineData("RainDown", "flash", ChaosMotion.RainDown)]
    [InlineData("RoamBounce", "glitch", ChaosMotion.RoamBounce)]
    [InlineData("Mixed", "flash", ChaosMotion.FloatUp)]
    public void TriggerSpecFollowsTheDashboardMotionSetting(string mode, string id, ChaosMotion expected)
    {
        var s = new AppSettings { BubbleTriggersEnabled = true, BubbleTriggerChance = 100, BubbleTriggerVariants = new() { id }, ChaosMotionMode = mode };
        Assert.Equal(expected, AmbientBubblePayloads.Roll(s, new Random(1), 0)!.Motion);
    }

    [Fact]
    public void MotionRowsTravelLikeWpf()
    {
        var spec = AmbientBubblePayloads.Build("flash")!;
        var rain = AmbientBubble.SpawnChaos(new Random(2), 0, 0, 0, 1920, 1080, 1, spec, ChaosMotion.RainDown);
        Assert.Equal(-rain.Size, rain.Y);   // just above the top
        var y = rain.Y; rain.Step(); Assert.True(rain.Y > y);
        var steps = 0;
        while (rain.Step() == AmbientBubble.Result.Alive && steps++ < 10000) { }
        Assert.True(rain.Y > 1080 + 50);   // left by the bottom, as a miss

        var side = AmbientBubble.SpawnChaos(new Random(3), 0, 0, 0, 1920, 1080, 1, spec, ChaosMotion.SideDrift);
        var x = side.X; side.Step();
        Assert.NotEqual(x, side.X);
        Assert.True(side.X < 0 || side.X > 1920 - side.Size);   // enters from a side edge

        var roam = AmbientBubble.SpawnChaos(new Random(4), 0, 0, 0, 1920, 1080, 1, spec, ChaosMotion.RoamBounce);
        for (var i = 0; i < 5000; i++)
        {
            Assert.Equal(AmbientBubble.Result.Alive, roam.Step());
            Assert.InRange(roam.X, 0, 1920 - roam.Size);
        }
    }

    [Fact]
    public void HeavyPayloadYieldsAndStopInvalidatesAlreadyPostedDelivery()
    {
        var runner = new AmbientPayloadRunner();
        Action? pending = null; var fired = 0;
        runner.Run(new VideoPayload(), () => true, () => false, () => false,
            a => pending = a, _ => throw new Exception(), _ => fired++);
        Assert.Equal(0, fired);
        runner.Stop(); pending!();
        Assert.Equal(0, fired);
    }

    [Theory]
    [InlineData(false, false, 1)]
    [InlineData(false, true, 0)]
    [InlineData(true, false, 0)]
    public void RainDeliveryRechecksServiceAndVideo(bool stopped, bool playing, int expected)
    {
        var runner = new AmbientPayloadRunner();
        Action? pending = null; var fired = 0; var running = true; var video = false;
        runner.Run(new VideoPayload(), () => running, () => video, () => true,
            _ => throw new Exception(), a => pending = a, _ => fired++);
        running = !stopped; video = playing;
        pending!();
        Assert.Equal(expected, fired);
    }

    [Fact]
    public void LightPayloadIsImmediateAndPlayingVideoRefusesHeavy()
    {
        var runner = new AmbientPayloadRunner(); var fired = new List<EffectPayload>();
        runner.Run(new FlashPayload(), () => true, () => true, () => true,
            _ => throw new Exception(), _ => throw new Exception(), fired.Add);
        runner.Run(new VideoPayload(), () => true, () => true, () => false,
            _ => throw new Exception(), _ => throw new Exception(), fired.Add);
        Assert.IsType<FlashPayload>(Assert.Single(fired));
    }

    [Fact]
    public void AmbientTreatExpiresWithoutPopCallbacksAndChaosPauseDoesNotFreezeIt()
    {
        var field = new AmbientBubbleField { HoldChaos = true };
        var b = new AmbientBubble { AmbientTrigger = true, Spec = new EffectBubbleSpec
            { Payload = new FlashPayload(), TreatLifeMs = 32 }, Size = 100 };
        field.Bubbles.Add(b);
        var calls = 0;
        field.ChaosDefused = field.ChaosDetonated = _ => calls++;
        field.Step();
        Assert.True(b.Popping);
        Assert.True(b.Deflating);
        Assert.Equal(0, calls);
    }

}
