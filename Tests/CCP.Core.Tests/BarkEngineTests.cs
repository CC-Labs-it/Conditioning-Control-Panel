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

    [Theory]
    [InlineData("FaceLost", true)]
    [InlineData("FaceFound", true)]
    [InlineData("Blink", true)]
    [InlineData("TrackingStateChanged", true)]
    [InlineData("IdleStateChanged", true)]
    [InlineData("ActivityChanged", true)]
    [InlineData("StillOnActivity", true)]
    [InlineData("TongueOut", false)]
    public void SensingClassificationReachesTheMouth(string trigger, bool unprompted)
    {
        Assert.True(Engine(Rule(trigger, "line")).Raise(trigger, guaranteed: true));
        Assert.Equal(unprompted, Assert.Single(_said).Unprompted);
    }

    [Fact]
    public void IdleRotatesAcrossRulesAndPersistsWithoutImmediateCycleRepeat()
    {
        var old = _s.BarkIdleRotation;
        try
        {
            _s.BarkIdleRotation = new();
            var rules = Enumerable.Range(0, 4).Select(i => Rule("Idle", "idle" + i)).ToArray();
            var e = Engine(rules);
            var clock = new PopQuizSchedulerTests.FakeClock();
            e.Clock = clock;
            for (int i = 0; i < 4; i++) { Assert.True(e.DispatchIdle()); clock.Advance(TimeSpan.FromSeconds(61)); }
            Assert.Equal(4, _said.Select(x => x.Text).Distinct().Count());
            Assert.Equal(4, _s.BarkIdleRotation.Count);
            var last = _said.Last().Text;
            Assert.True(e.DispatchIdle());
            Assert.NotEqual(last, _said.Last().Text);
        }
        finally { _s.BarkIdleRotation = old; }
    }

    [Fact]
    public void IdleSkipsMuteAndSpeakingAndUsesConditions()
    {
        var rule = Rule("Idle", "eligible");
        rule.Conditions = new() { ["player_level_gte"] = _s.PlayerLevel + 1 };
        var e = Engine(rule);
        Assert.False(e.DispatchIdle());
        rule.Conditions = null;
        _s.MasterVolume = 0;
        Assert.False(e.DispatchIdle());
        _s.MasterVolume = 50;
        e.IsSpeaking = () => true;
        Assert.False(e.DispatchIdle());
        e.IsSpeaking = () => false;
        Assert.True(e.DispatchIdle());
    }

    [Fact]
    public void IdlePrefersEligibleGatedLinesDespiteAnUngatedMajority()
    {
        var old = _s.BarkIdleRotation;
        int gated = 0;
        try
        {
            var rules = Enumerable.Range(0, 32).Select(i => Rule("Idle", "plain")).ToList();
            var band = Rule("Idle", "band");
            band.Conditions = new() { ["player_level_gte"] = 0 };
            rules.Add(band);
            for (int seed = 0; seed < 200; seed++)
            {
                _s.BarkIdleRotation = new();
                var e = new BarkEngine(() => new BarkRuleSet(rules), new Random(seed))
                { Speak = speech => { if (speech.Text == "band") gated++; }, DryRun = false };
                e.Start();
                Assert.True(e.DispatchIdle());
            }
            Assert.InRange(gated, 45, 110); // uniform selection would yield only about six
        }
        finally { _s.BarkIdleRotation = old; }
    }

    [Fact]
    public void DryRunExercisesSessionGatesWithoutSpeechOrPersistedWrites()
    {
        var rule = Rule("T", "silent");
        rule.Repeatable = false;
        rule.ScopeRaw = "lifetime";
        var e = Engine(rule);
        e.DryRun = true;
        var before = _s.BarkVariantRotation.Count;
        Assert.False(e.Raise("T"));
        Assert.Empty(_said);
        Assert.False(_s.IsBarkFired(rule.Id));
        Assert.Equal(before, _s.BarkVariantRotation.Count);
        e.DryRun = false;
        Assert.False(e.Raise("T")); // dry-run still spends the in-memory one-shot
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

    [Fact]   // WPF AudioService.MarkWhisperAudio (:948) + EvaluateGate (:1668): a whisper holds the floor for its length + 0.25 s
    public void AnAudibleWhisperHoldsOrdinaryBarksButNotGuaranteedOrAttributionLines()
    {
        var clock = new PopQuizSchedulerTests.FakeClock();
        var e = Engine(Rule("AttentionCheckFail", "eyes"), Rule("PossessionEffect", "mine"));
        e.Clock = clock;
        e.MarkWhisperAudio(double.NaN);
        e.MarkWhisperAudio(-1);
        Assert.False(e.IsWhisperAudioPlaying);
        e.MarkWhisperAudio(3);
        e.MarkWhisperAudio(1);   // a shorter clip never cuts the longer window short
        Assert.False(e.Raise("AttentionCheckFail"));
        Assert.True(e.Raise("PossessionEffect"));   // text-only attribution, never a second voice
        clock.Advance(TimeSpan.FromSeconds(3.2));
        Assert.True(e.IsWhisperAudioPlaying);
        Assert.False(e.Raise("AttentionCheckFail"));
        Assert.True(e.Raise("AttentionCheckFail", guaranteed: true));
        clock.Advance(TimeSpan.FromSeconds(0.1));
        Assert.False(e.IsWhisperAudioPlaying);
    }

    [Fact]
    public void IdleRotationPersistsAcrossEnginesAndHonorsSpeechAndVolumeGates()
    {
        var saved = _s.BarkIdleRotation;
        _s.BarkIdleRotation = new List<string>();
        try
        {
            var a = Rule("Idle", "a");
            var b = Rule("Idle", "b");
            a.Priority = b.Priority = 100;
            var e = Engine(a, b);
            e.IsSpeaking = () => true;
            Assert.False(e.DispatchIdle());
            e.IsSpeaking = () => false;
            _s.MasterVolume = 0;
            Assert.False(e.DispatchIdle());
            _s.MasterVolume = 50;
            Assert.True(e.DispatchIdle());
            Assert.Single(_s.BarkIdleRotation);
            Assert.True(Engine(a, b).DispatchIdle());
            Assert.Equal(2, _said.Select(x => x.Text).Distinct().Count());
            Assert.Equal(2, _s.BarkIdleRotation.Count);
        }
        finally { _s.BarkIdleRotation = saved; }
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
