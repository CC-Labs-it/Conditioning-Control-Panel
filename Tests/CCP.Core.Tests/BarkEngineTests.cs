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
    private readonly List<string> _idle, _latches;
    private readonly Dictionary<string, List<string>> _rotation;

    public BarkEngineTests()
    {
        _old = (_s.AvatarEnabled, _s.MasterVolume, _s.BarkChatSuppressionMs);
        _idle = new(_s.BarkIdleRotation);
        _latches = new(_s.BarkLifetimeFired);
        _rotation = _s.BarkVariantRotation.ToDictionary(kv => kv.Key, kv => new List<string>(kv.Value));
        (_s.AvatarEnabled, _s.MasterVolume, _s.BarkChatSuppressionMs) = (true, 50, 0);
    }

    public void Dispose()
    {
        (_s.AvatarEnabled, _s.MasterVolume, _s.BarkChatSuppressionMs) = _old;
        _s.BarkIdleRotation = _idle;
        _s.BarkLifetimeFired = _latches;
        _s.BarkVariantRotation = _rotation;
    }

    private static BarkRule Rule(string trigger, params string[] lines) => new()
    { Id = trigger + Guid.NewGuid().ToString("N"), Trigger = trigger, VariantPool = lines.Select(l => new BarkVariant(l)).ToList() };

    private BarkEngine Engine(params BarkRule[] rules)
    {
        var e = new BarkEngine(() => new BarkRuleSet(rules), new Random(7)) { Speak = _said.Add };
        e.Start();
        return e;
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
    public void IdleRulesRotateAcrossSessionsThenRecycleWithoutAnImmediateRepeat()
    {
        var rules = new[] { Rule("Idle", "a"), Rule("Idle", "b"), Rule("Idle", "c") };
        var clock = new PopQuizSchedulerTests.FakeClock();
        Assert.True(Engine(rules).DispatchIdle());
        var e = Engine(rules); // reload the persisted rule-level rotation
        e.Clock = clock;
        for (int i = 0; i < 2; i++)
        {
            Assert.True(e.DispatchIdle());
            clock.Advance(TimeSpan.FromMinutes(1));
        }
        Assert.Equal(3, _said.Select(x => x.Text).Distinct().Count());
        Assert.Equal(3, rules.Count(r => _s.BarkIdleRotation.Contains(r.Id)));
        Assert.True(e.DispatchIdle());
        Assert.NotEqual(_said[2].Text, _said[3].Text);
        Assert.All(_said, line => Assert.True(line.Unprompted));
        var only = Engine(Rule("Idle", "only"));
        only.Clock = clock;
        Assert.True(only.DispatchIdle());
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(only.DispatchIdle()); // a single eligible rule necessarily repeats
    }

    private sealed class IdleRandom(double roll) : Random
    {
        public override double NextDouble() => roll;
        public override int Next(int maxValue) => 0;
    }

    [Theory]
    [InlineData(0.34999, "gated")]
    [InlineData(0.35, "ordinary")]
    public void IdleBiasUsesTheWpfThresholdAndOnlyEligibleRules(double roll, string expected)
    {
        var ordinary = Rule("Idle", "ordinary");
        ordinary.Priority = 10;
        var gated = Rule("Idle", "gated");
        gated.Conditions = new() { ["master_volume_gte"] = 50 };
        var excluded = Rule("Idle", "ineligible");
        excluded.Conditions = new() { ["master_volume_gt"] = 50 };
        excluded.Priority = 100;
        var e = new BarkEngine(() => new BarkRuleSet(new[] { ordinary, gated, excluded }), new IdleRandom(roll)) { Speak = _said.Add };
        e.Start();
        Assert.True(e.DispatchIdle());
        Assert.Equal(expected, Assert.Single(_said).Text);
    }

    [Fact]
    public void IdleRespectsMuteSpeakingChatAndThePanicHoldWithoutSpendingTheRule()
    {
        var idle = Rule("Idle", "idle");
        var panic = Rule("Panic", "safe");
        panic.ClassRaw = "safety";
        var e = Engine(idle, panic);
        var clock = new PopQuizSchedulerTests.FakeClock();
        e.Clock = clock;
        _s.MasterVolume = 0;
        Assert.False(e.DispatchIdle());
        _s.MasterVolume = 50;
        e.IsSpeaking = () => true;
        Assert.False(e.DispatchIdle());
        e.IsSpeaking = () => false;
        _s.BarkChatSuppressionMs = 1000;
        e.NoteUserMessage();
        Assert.False(e.DispatchIdle());
        Assert.DoesNotContain(idle.Id, _s.BarkIdleRotation);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(e.Raise("Panic"));
        Assert.False(e.DispatchIdle());
        clock.Advance(TimeSpan.FromSeconds(6));
        Assert.False(e.DispatchIdle()); // hold expired, but ordinary idle still keeps the 60 s floor
        clock.Advance(TimeSpan.FromSeconds(54));
        Assert.True(e.DispatchIdle());
    }

    [Theory]
    [InlineData("AttentionCheckFail", false)]
    [InlineData("PossessionEffect", true)]
    [InlineData("PossessionTripwire", true)]
    [InlineData("PossessionWarden", true)]
    [InlineData("PossessionRungChanged", true)]
    [InlineData("PossessionTimerRestarted", true)]
    [InlineData("PossessionRemember", true)]
    [InlineData("LockdownConscript", true)]
    [InlineData("EmergencyExitOpened", true)]
    [InlineData("EmergencyExitVerdict", true)]
    [InlineData("Idle", false)]
    public void WhisperExemptsOnlyAttributionsWhileNarratorHoldsThemAll(string trigger, bool maySpeak)
    {
        var e = Engine(Rule(trigger, "line"));
        e.NoteWhisperAudio(TimeSpan.FromMinutes(1));
        Assert.Equal(maySpeak, e.Raise(trigger));
        e.IsChaosNarratorPlaying = () => true;
        Assert.False(e.Raise(trigger));
        Assert.True(e.Raise(trigger, guaranteed: true));
        var safety = Rule("Panic", "safe");
        safety.ClassRaw = "safety";
        e = Engine(safety);
        e.NoteWhisperAudio(TimeSpan.FromMinutes(1));
        e.IsChaosNarratorPlaying = () => true;
        Assert.True(e.Raise("Panic"));
    }

    [Fact]
    public void TierOneShotsUseTheLiveTierAndKeepTheWpfSessionLatch()
    {
        var r = Rule("AttentionCheckFail", "tier");
        (r.Repeatable, r.ScopeRaw) = (false, "tier");
        var e = Engine(r);
        var tier = "Level1";
        e.CurrentTier = () => tier;
        Assert.True(e.Raise(r.Trigger));
        Assert.True(_s.IsBarkFired(r.Id + "@Level1"));
        tier = "Level2";
        Assert.False(e.Raise(r.Trigger, guaranteed: true)); // session latch remains, as WPF
        e.ReloadRules();
        Assert.True(e.Raise(r.Trigger));
        Assert.True(_s.IsBarkFired(r.Id + "@Level2"));
        e = Engine(r);
        e.CurrentTier = () => "Level1";
        Assert.False(e.Raise(r.Trigger, guaranteed: true));
    }

    [Fact]
    public void DryRunExercisesGatesWithoutMouthEchoOrPersistedMutation()
    {
        var idle = Rule("Idle", "idle");
        var once = Rule("AttentionCheckFail", "once");
        (once.Repeatable, once.ScopeRaw) = (false, "lifetime");
        var e = Engine(idle, once);
        e.DryRun = true;
        bool echoed = false;
        e.BarkSpoken += (_, _) => echoed = true;
        Assert.False(e.DispatchIdle());
        Assert.False(e.Raise(once.Trigger));
        Assert.False(e.DispatchIdle()); // gap still evaluated
        Assert.Empty(_said);
        Assert.False(echoed);
        Assert.DoesNotContain(idle.Id, _s.BarkIdleRotation);
        Assert.False(_s.BarkVariantRotation.ContainsKey(idle.Id));
        Assert.False(_s.IsBarkFired(once.Id));
        e.DryRun = false;
        Assert.False(e.Raise(once.Trigger, guaranteed: true)); // dry-run kept its session latch
    }

    [Fact]
    public void EnvironmentOptInStartsInDryRun()
    {
        var previous = Environment.GetEnvironmentVariable("CCP_BARK_DRYRUN");
        try
        {
            Environment.SetEnvironmentVariable("CCP_BARK_DRYRUN", "1");
            var e = Engine(Rule("AttentionCheckFail", "silent"));
            Assert.True(e.DryRun);
            Assert.False(e.Raise("AttentionCheckFail"));
            Assert.Empty(_said);
        }
        finally { Environment.SetEnvironmentVariable("CCP_BARK_DRYRUN", previous); }
    }

    [Theory]
    [InlineData(false, false, false, false, false, true)]
    [InlineData(true, false, false, false, false, false)]
    [InlineData(true, true, false, false, false, true)]
    [InlineData(true, false, true, false, false, true)]
    [InlineData(true, false, true, true, false, false)]
    [InlineData(true, false, false, false, true, true)]
    public void IdleVoiceHonorsEngineSessionPauseAndOptIn(bool idle, bool engine, bool session,
        bool paused, bool optedIn, bool expected) =>
        Assert.Equal(expected, ConditioningControlPanel.Services.Companion.CompanionIdleVoicePolicy.ShouldVoice(idle, engine, session, paused, optedIn, panicQuiet: false));

    [Fact]
    public void WhisperDurationExtendsTheFloorAndIncludesItsQuarterSecondTail()
    {
        var e = Engine(Rule("AttentionCheckFail", "line"));
        var clock = new PopQuizSchedulerTests.FakeClock();
        e.Clock = clock;
        e.NoteWhisperAudio(TimeSpan.FromSeconds(10));
        e.NoteWhisperAudio(TimeSpan.FromSeconds(1)); // never shorten an audible clip
        e.NoteWhisperAudio(TimeSpan.Zero);
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.False(e.Raise("AttentionCheckFail"));
        clock.Advance(TimeSpan.FromMilliseconds(250));
        Assert.True(e.Raise("AttentionCheckFail"));
    }

    [Fact]
    public void PanicQuietOverridesEveryIdleOptInButNeverPromptedReplies()
    {
        Assert.False(ConditioningControlPanel.Services.Companion.CompanionIdleVoicePolicy.ShouldVoice(true, true, true, false, true, true));
        Assert.True(ConditioningControlPanel.Services.Companion.CompanionIdleVoicePolicy.ShouldVoice(false, false, false, true, false, true));
        Assert.True(ConditioningControlPanel.Services.Companion.CompanionIdleVoicePolicy.IsPanicQuiet(300999, 301000));
        Assert.False(ConditioningControlPanel.Services.Companion.CompanionIdleVoicePolicy.IsPanicQuiet(301000, 301000));
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
