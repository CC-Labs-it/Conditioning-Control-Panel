using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.Bark;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Core BarkEngine against WPF 7.1.5 BarkService: priority walk, conditions, gate, rotation, latches.</summary>
[Collection(SessionStatics.Name)]
public sealed class BarkEngineTests : IDisposable
{
    private readonly ConditioningControlPanel.Models.AppSettings _s = CoreSettings.Current;
    private readonly (bool, int, int) _old;
    private readonly List<BarkSpeech> _said = new();

    public BarkEngineTests()
    {
        _old = (_s.AvatarEnabled, _s.MasterVolume, _s.BarkChatSuppressionMs);
        (_s.AvatarEnabled, _s.MasterVolume, _s.BarkChatSuppressionMs) = (true, 50, 0);
    }

    public void Dispose() => (_s.AvatarEnabled, _s.MasterVolume, _s.BarkChatSuppressionMs) = _old;

    private static BarkRule Rule(string trigger, params string[] lines) => new()
    { Id = trigger + Guid.NewGuid().ToString("N"), Trigger = trigger, VariantPool = lines.Select(l => new BarkVariant(l)).ToList() };

    private BarkEngine Engine(params BarkRule[] rules)
    {
        var e = new BarkEngine(() => new BarkRuleSet(rules), new Random(7)) { Speak = _said.Add };
        e.Start();
        return e;
    }

    [Fact]
    public void IdleVisitsEveryEligibleRuleBeforeRecyclingAndPersistsAcrossLaunches()
    {
        var oldIdle = _s.BarkIdleRotation;
        try
        {
            _s.BarkIdleRotation = new();
            var rules = new[] { Rule("Idle", "a"), Rule("Idle", "b"), Rule("Idle", "c") };
            var clock = new PopQuizSchedulerTests.FakeClock();
            for (int i = 0; i < 3; i++)
            {
                var e = Engine(rules);
                e.Clock = clock;
                Assert.True(e.DispatchIdle());
            }
            Assert.Equal(3, _said.Select(x => x.Text).Distinct().Count());
            Assert.Equal(3, _s.BarkIdleRotation.Count);
            var same = Engine(rules);
            same.Clock = clock;
            Assert.True(same.DispatchIdle());
            // A new process does not persist the last-id reseed (same as WPF).
            clock.Advance(TimeSpan.FromSeconds(61));
            Assert.True(same.DispatchIdle());
            Assert.NotEqual(_said[^2].Text, _said[^1].Text);
        }
        finally { _s.BarkIdleRotation = oldIdle; }
    }

    [Fact]
    public void IdleNeverSpeaksWhileMutedOrSpeakingAndKeepsNormalTimingGates()
    {
        var e = Engine(Rule("Idle", "idle"));
        _s.MasterVolume = 0;
        Assert.False(e.DispatchIdle());
        _s.MasterVolume = 50;
        e.IsSpeaking = () => true;
        Assert.False(e.DispatchIdle());
        e.IsSpeaking = () => false;
        Assert.True(e.DispatchIdle());
        Assert.False(e.DispatchIdle());
        Assert.Single(_said);
    }

    [Fact]
    public void DryRunSimulatesCooldownsWithoutSpeakingOrPersistingLatchesAndRotation()
    {
        var r = Rule("T", "silent");
        (r.Repeatable, r.ScopeRaw) = (false, "lifetime");
        var e = Engine(r);
        e.DryRun = true;
        Assert.False(e.Raise("T"));
        Assert.False(_s.IsBarkFired(r.Id));
        Assert.False(_s.BarkVariantRotation.ContainsKey(r.Id));
        Assert.Empty(_said);
        e.DryRun = false;
        Assert.False(e.Raise("T", guaranteed: true)); // in-memory one-shot simulated
        Assert.True(Engine(r).Raise("T")); // persisted state untouched
    }

    private sealed class IdleRandom(double roll) : Random
    {
        public override double NextDouble() => roll;
        public override int Next(int maxValue) => 0;
    }

    [Theory]
    [InlineData(0.349, "gated")]
    [InlineData(0.35, "plain")]
    public void IdleBiasUsesTheWpfThirtyFivePercentBoundary(double roll, string expected)
    {
        var idle = _s.BarkIdleRotation;
        try
        {
            _s.BarkIdleRotation = new();
            var plain = Rule("Idle", "plain");
            var gated = Rule("Idle", "gated");
            gated.Conditions = new() { ["master_volume_gt"] = 0 };
            var impossible = Rule("Idle", "ineligible");
            impossible.Conditions = new() { ["master_volume_lt"] = 0 };
            var e = new BarkEngine(() => new BarkRuleSet(new[] { plain, gated, impossible }), new IdleRandom(roll))
            { Speak = _said.Add };
            e.Start();
            Assert.True(e.DispatchIdle());
            Assert.Equal(expected, Assert.Single(_said).Text);
        }
        finally { _s.BarkIdleRotation = idle; }
    }

    [Theory]
    [InlineData("T", false)]
    [InlineData("PossessionEffect", true)]
    [InlineData("LockdownConscript", true)]
    [InlineData("EmergencyExitOpened", true)]
    [InlineData("EmergencyExitVerdict", true)]
    [InlineData("PossessionUnknown", false)]
    public void WhisperFloorExemptsOnlyTextAttributionsAndNarratorStillHoldsThem(string trigger, bool allowed)
    {
        var e = Engine(Rule(trigger, "line"));
        e.WhisperPlaying = () => true;
        Assert.Equal(allowed, e.Raise(trigger));
        e = Engine(Rule(trigger, "line"));
        e.NarratorPlaying = () => true;
        Assert.False(e.Raise(trigger));
        Assert.True(e.Raise(trigger, guaranteed: true));
    }

    [Fact]
    public void TierLatchUsesCurrentTierAcrossEngineRestarts()
    {
        var r = Rule("T", "thanks");
        (r.Repeatable, r.ScopeRaw) = (false, "tier");
        var e = Engine(r);
        e.CurrentTier = () => "Level1";
        Assert.True(e.Raise("T"));
        Assert.True(_s.IsBarkFired(r.Id + "@Level1"));
        e = Engine(r);
        e.CurrentTier = () => "Level1";
        Assert.False(e.Raise("T", guaranteed: true));
        e = Engine(r);
        e.CurrentTier = () => "Level2";
        Assert.True(e.Raise("T"));
    }

    [Fact]
    public void TheHighestPriorityRuleWhoseConditionsPassSpeaksWithSubstitutions()
    {
        var high = Rule("T", "level {level} in {0}");
        (high.Priority, high.Conditions) = (100, new Dictionary<string, object> { ["level_gte"] = 10, ["mood_eq"] = "HAPPY" });
        var low = Rule("T", "low");
        Assert.True(Engine(low, high).Raise("T", c => c.Set("level", 12).Set("mood", "happy")));
        Assert.True(Engine(low, high).Raise("T", c => c.Set("level", 9.5).Set("mood", "happy")));
        Assert.Equal(new[] { "level 12 in that", "low" }, _said.Select(x => x.Text));
        Assert.True(_said[0].Priority);
        Assert.False(_said[1].Priority);
    }

    [Fact]
    public void TheGateHoldsCompanionOffTheSixtySecondFloorAndCooldownsButNotExemptOrGuaranteedLines()
    {
        var plain = Rule("T", "a");
        var check = Rule("AttentionCheckFail", "eyes");
        check.CooldownMs = 20000;
        var e = Engine(plain, check);
        _s.AvatarEnabled = false;
        Assert.False(e.Raise("T"));
        _s.AvatarEnabled = true;
        Assert.True(e.Raise("T"));
        Assert.False(e.Raise("T"));                     // 60 s floor
        Assert.True(e.Raise("T", guaranteed: true));    // guaranteed skips timing
        Assert.True(e.Raise("AttentionCheckFail"));     // exempt from the floor...
        Assert.False(e.Raise("AttentionCheckFail"));    // ...but not from its own cooldown
        Assert.Equal(new[] { "a", "a", "eyes" }, _said.Select(x => x.Text));
    }

    [Fact]
    public void AOneShotLatchOutlivesTheSessionAndASafetyBarkHoldsTheFloor()
    {
        var once = Rule("AttentionCheckFail", "once");
        (once.Repeatable, once.ScopeRaw) = (false, "lifetime");
        Assert.True(Engine(once).Raise("AttentionCheckFail"));
        Assert.False(Engine(once).Raise("AttentionCheckFail", guaranteed: true));   // a new session, still latched

        var panic = Rule("Panic", "safe");
        panic.ClassRaw = "safety";
        var check = Rule("AttentionCheckFail", "after");
        var e = Engine(panic, check);
        _s.AvatarEnabled = false;
        Assert.True(e.Raise("Panic"));
        _s.AvatarEnabled = true;
        Assert.False(e.Raise("AttentionCheckFail"));    // safety-active for 6 s
    }

    [Fact]
    public void VariantsRotateWithoutRepeatAcrossSessionsAndHiddenOrUnheardLinesSpendNothing()
    {
        var r = Rule("AttentionCheckFail", "a", "b", "c");
        Assert.True(Engine(r).Raise("AttentionCheckFail"));
        Assert.True(Engine(r).Raise("AttentionCheckFail"));   // restored from BarkVariantRotation
        Assert.True(Engine(r).Raise("AttentionCheckFail"));
        Assert.Equal(new[] { "a", "b", "c" }, _said.Select(x => x.Text).OrderBy(x => x));

        var hidden = Rule("AttentionCheckFail", "x");
        _s.DisabledPhraseIds.Add(BarkEngine.BarkLineId(hidden.Id, hidden.VariantPool![0]));
        Assert.False(Engine(hidden).Raise("AttentionCheckFail"));

        var once = Rule("AttentionCheckFail", "y");
        (once.Repeatable, once.ScopeRaw) = (false, "lifetime");
        var mute = new BarkEngine(() => new BarkRuleSet(new[] { once }));
        mute.Start();
        Assert.False(mute.Raise("AttentionCheckFail"));      // no mouth: the latch is not burned on silence
        Assert.False(_s.IsBarkFired(once.Id));
    }

    [Fact]   // WPF CommitFire (:1793): a safety line holds every other bark for 6 s on the engine's clock
    public void APanicSafetyLineHoldsEveryOtherBarkForSixSeconds()
    {
        var panic = Rule("Panic", "breathe");
        panic.ClassRaw = "safety";
        var clock = new PopQuizSchedulerTests.FakeClock();
        var e = Engine(panic, Rule("AttentionCheckFail", "eyes"));
        e.Clock = clock;
        Assert.True(e.Raise("Panic"));
        clock.Advance(TimeSpan.FromSeconds(5.9));
        Assert.False(e.Raise("AttentionCheckFail"));
        clock.Advance(TimeSpan.FromSeconds(0.2));
        Assert.True(e.Raise("AttentionCheckFail"));
    }

    [Fact]
    public void AModOverlayMergesFieldByFieldOverTheBaseRule()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(dir, "base.json"),
            """[{"id":"r1","trigger":"LevelUp","priority":5,"conditions":{"level_gte":3},"variant_pool":["base"]}]""");
        File.WriteAllText(Path.Combine(dir, "mod.json"), """[{"id":"r1","priority":null,"variant_pool":["skin"]}]""");
        var rule = Assert.Single(BarkRuleManifest.Load(Path.Combine(dir, "base.json"), Path.Combine(dir, "mod.json")).AllRules);
        Assert.Equal(("LevelUp", 5, "skin"), (rule.Trigger, rule.Priority, rule.VariantPool![0].Text));
        Assert.True(rule.Conditions!.ContainsKey("level_gte"));
        Directory.Delete(dir, true);
    }
}
