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
    public void DryRunExercisesTheGateWithoutSpeakingOrPersisting()
    {
        var old = Environment.GetEnvironmentVariable("CCP_BARK_DRYRUN");
        var r = Rule("T", "dry");
        (r.Repeatable, r.ScopeRaw) = (false, "lifetime");
        try
        {
            Environment.SetEnvironmentVariable("CCP_BARK_DRYRUN", "1");
            var e = Engine(r);
            Assert.False(e.Raise("T"));
            Assert.Empty(_said);
            Assert.False(_s.IsBarkFired(r.Id));
            Assert.False(_s.BarkVariantRotation.ContainsKey(r.Id));
            Environment.SetEnvironmentVariable("CCP_BARK_DRYRUN", null);
            Assert.True(Engine(r).Raise("T"));
        }
        finally { Environment.SetEnvironmentVariable("CCP_BARK_DRYRUN", old); }
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
    public void IdleRulesRotateAcrossLaunchesAndRecycleWithoutImmediateRepeat()
    {
        var old = _s.BarkIdleRotation;
        try
        {
            _s.BarkIdleRotation = new();
            var a = Rule("Idle", "a");
            var b = Rule("Idle", "b");
            var c = Rule("Idle", "c");
            Engine(a, b, c).DispatchIdle();
            Engine(a, b, c).DispatchIdle();
            var e = Engine(a, b, c);
            var clock = new PopQuizSchedulerTests.FakeClock();
            e.Clock = clock;
            e.DispatchIdle();
            Assert.Equal(3, _said.Select(x => x.Text).Distinct().Count());
            var last = _said[^1].Text;
            clock.Advance(TimeSpan.FromSeconds(61));
            e.DispatchIdle();
            Assert.NotEqual(last, _said[^1].Text);
        }
        finally { _s.BarkIdleRotation = old; }
    }

    private sealed class GatedRandom : Random
    {
        public override double NextDouble() => 0.34;
        public override int Next(int maxValue) => 0;
    }

    [Fact]
    public void IdlePrefersEligibleGatedRulesAndNeverSpendsWhileMutedSpeakingOrDry()
    {
        var a = Rule("Idle", "plain");
        var gated = Rule("Idle", "gated");
        gated.Conditions = new() { ["player_level_gte"] = 0 };
        var e = new BarkEngine(() => new BarkRuleSet(new[] { a, gated }), new GatedRandom()) { Speak = _said.Add };
        e.Start();
        _s.MasterVolume = 0;
        e.DispatchIdle();
        _s.MasterVolume = 50;
        e.IsSpeaking = () => true;
        e.DispatchIdle();
        Assert.Empty(_said);
        e.IsSpeaking = () => false;
        e.DispatchIdle();
        Assert.Equal("gated", Assert.Single(_said).Text);
        e.DispatchIdle(); // min gap: no second line
        Assert.Single(_said);
        var old = _s.BarkIdleRotation.ToArray();
        var dry = Engine(a);
        dry.DryRun = true;
        dry.DispatchIdle();
        Assert.Equal(old, _s.BarkIdleRotation);
        Assert.Single(_said);
    }

    [Fact]
    public void WhisperFloorExtendsThroughTheTailWithOnlyTheWpfBypasses()
    {
        var panic = Rule("Panic", "safe");
        panic.ClassRaw = "safety";
        var e = Engine(panic, Rule("AttentionCheckFail", "eyes"), Rule("PossessionEffect", "haunt"), Rule("EmergencyExitVerdict", "door"));
        var clock = new PopQuizSchedulerTests.FakeClock();
        e.Clock = clock;
        e.MarkWhisperAudio(10);
        e.MarkWhisperAudio(1); // cannot shorten an audible clip
        e.MarkWhisperAudio(double.NaN);
        Assert.False(e.Raise("AttentionCheckFail")); // global-gap exemption does not bypass the whisper
        Assert.True(e.Raise("PossessionEffect"));
        Assert.True(e.Raise("EmergencyExitVerdict"));
        Assert.True(e.Raise("Panic"));
        clock.Advance(TimeSpan.FromSeconds(10.24));
        Assert.False(e.Raise("AttentionCheckFail"));
        clock.Advance(TimeSpan.FromSeconds(0.02));
        Assert.True(e.Raise("AttentionCheckFail"));
        e.MarkWhisperAudio(5);
        Assert.True(e.Raise("AttentionCheckFail", guaranteed: true));
    }

    [Fact]
    public void TierLatchesPersistUnderTheActualTierAndRearmOnANewLaunchAtAnotherTier()
    {
        var r = Rule("T", "tier");
        (r.Repeatable, r.ScopeRaw) = (false, "tier");
        BarkEngine At(string tier) { var e = Engine(r); e.CurrentTier = () => tier; return e; }
        Assert.True(At("Level1").Raise("T"));
        Assert.True(_s.IsBarkFired(r.Id + "@Level1"));
        Assert.False(At("Level1").Raise("T", guaranteed: true));
        Assert.True(At("Level2").Raise("T"));
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
