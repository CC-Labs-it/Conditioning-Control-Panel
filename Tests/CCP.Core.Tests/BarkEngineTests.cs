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

    private sealed class DecisionSink : Serilog.Core.ILogEventSink {
        public readonly List<Serilog.Events.LogEvent> Events = new();
        public void Emit(Serilog.Events.LogEvent e) => Events.Add(e);
    }
    private sealed class IdleRandom(double roll) : Random {
        public override double NextDouble() => roll;
        public override int Next(int maxValue) => maxValue - 1;
    }
    [Theory]
    [InlineData(0.349, "gated")]
    [InlineData(0.35, "plain")]
    public void IdleGatedPreferenceHasTheWpfThirtyFivePercentBoundary(double roll, string expected)
    {
        var saved = _s.BarkIdleRotation;
        try {
            _s.BarkIdleRotation = new();
            var gated = Rule("Idle", "gated");
            gated.Conditions = new() { ["master_volume_gt"] = 0 };
            var plain = Rule("Idle", "plain");
            var e = new BarkEngine(() => new BarkRuleSet(new[] { gated, plain }), new IdleRandom(roll)) { Speak = _said.Add };
            e.Start();
            Assert.True(e.DispatchIdle());
            Assert.Equal(expected, Assert.Single(_said).Text);
        }
        finally { _s.BarkIdleRotation = saved; }
    }
    [Fact]
    public void WhisperHoldsOrdinaryBarksUntilTheLongestClipAndTailButSafetyAndGuaranteedBypass()
    {
        var clock = new PopQuizSchedulerTests.FakeClock();
        var panic = Rule("Panic", "safe");
        panic.ClassRaw = "safety";
        var e = Engine(Rule("AttentionCheckFail", "eyes"), panic);
        e.Clock = clock;
        e.MarkWhisperAudio(10);
        e.MarkWhisperAudio(1);
        e.MarkWhisperAudio(double.NaN);
        e.MarkWhisperAudio(0);
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.False(e.Raise("AttentionCheckFail"));
        clock.Advance(TimeSpan.FromSeconds(0.25));
        Assert.True(e.Raise("AttentionCheckFail"));
        e.MarkWhisperAudio(10);
        Assert.True(e.Raise("AttentionCheckFail", guaranteed: true));
        Assert.True(e.Raise("Panic"));
    }
    [Fact]
    public void DecisionLogsHaveWpfMetadataAndLengthButNeverText()
    {
        var old = Serilog.Log.Logger;
        var sink = new DecisionSink();
        using var logger = new Serilog.LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(sink).CreateLogger();
        Serilog.Log.Logger = logger;
        try {
            var r = Rule("Log", "private companion words");
            r.Mood = "happy";
            var e = Engine(r);
            Assert.True(e.Raise("Log"));
            Assert.False(e.Raise("Log"));
            var fire = Assert.Single(sink.Events, x => x.Properties.ContainsKey("Chars"));
            Assert.Equal("23", fire.Properties["Chars"].ToString());
            Assert.Contains("Mood", fire.Properties.Keys);
            Assert.Contains("Priority", fire.Properties.Keys);
            Assert.Contains(sink.Events, x => x.Properties.ContainsKey("Reason"));
            Assert.DoesNotContain(sink.Events, x => x.RenderMessage().Contains("private companion words"));
        }
        finally { Serilog.Log.Logger = old; }
    }
    [Fact]
    public void TierLatchUsesTheRawAccountTierAndRetainsTheWpfSessionLatch()
    {
        var old = CoreAccount.CurrentTierProvider;
        try {
            var r = Rule("Tier", "hello");
            (r.Repeatable, r.ScopeRaw) = (false, "tier");
            CoreAccount.CurrentTierProvider = () => ConditioningControlPanel.Models.PatreonTier.Level1;
            var e = Engine(r);
            Assert.True(e.Raise("Tier", guaranteed: true));
            Assert.True(_s.IsBarkFired(r.Id + "@Level1"));
            Assert.False(Engine(r).Raise("Tier", guaranteed: true));
            CoreAccount.CurrentTierProvider = () => ConditioningControlPanel.Models.PatreonTier.Level2;
            Assert.False(e.Raise("Tier", guaranteed: true)); // WPF also keeps a session latch
            Assert.True(Engine(r).Raise("Tier", guaranteed: true));
            Assert.True(_s.IsBarkFired(r.Id + "@Level2"));
            CoreAccount.CurrentTierProvider = () => throw new InvalidOperationException();
            Assert.Equal(ConditioningControlPanel.Models.PatreonTier.None, CoreAccount.CurrentTier);
        }
        finally { CoreAccount.CurrentTierProvider = old; }
    }
    [Fact]
    public void IdleRotationSurvivesRestartRecyclesWithoutImmediateRepeatAndHonorsTheFloor()
    {
        var saved = _s.BarkIdleRotation;
        try {
            _s.BarkIdleRotation = new();
            var rules = new[] { Rule("Idle", "a"), Rule("Idle", "b"), Rule("Idle", "c") };
            var clock = new PopQuizSchedulerTests.FakeClock();
            var e = Engine(rules);
            e.Clock = clock;
            Assert.True(e.DispatchIdle());
            Assert.False(e.DispatchIdle());
            clock.Advance(TimeSpan.FromSeconds(60));
            Assert.True(e.DispatchIdle());
            Assert.True(Engine(rules).DispatchIdle()); // third rule after restart
            Assert.Equal(3, _said.Select(x => x.Text).Distinct().Count());
            e = Engine(rules);
            e.Clock = clock;
            Assert.True(e.DispatchIdle()); // recycle
            var last = _said.Last().Text;
            clock.Advance(TimeSpan.FromSeconds(60));
            Assert.True(e.DispatchIdle());
            Assert.NotEqual(last, _said.Last().Text);
        }
        finally { _s.BarkIdleRotation = saved; }
    }
    [Fact]
    public void IdleSkipsMutedSpeakingAndIneligibleRulesWithoutSpendingThem()
    {
        var r = Rule("Idle", "eligible");
        var no = Rule("Idle", "wrong");
        no.Conditions = new() { ["player_level_gt"] = 999999 };
        var e = Engine(r, no);
        _s.MasterVolume = 0;
        Assert.False(e.DispatchIdle());
        _s.MasterVolume = 50;
        e.IsSpeaking = () => true;
        Assert.False(e.DispatchIdle());
        e.IsSpeaking = () => false;
        Assert.True(e.DispatchIdle());
        Assert.Equal("eligible", Assert.Single(_said).Text);
        Assert.True(_said[0].Unprompted);
        Assert.DoesNotContain(no.Id, _s.BarkIdleRotation);
    }
    [Theory]
    [InlineData("PossessionEffect"), InlineData("PossessionTripwire"), InlineData("PossessionWarden")]
    [InlineData("PossessionRungChanged"), InlineData("PossessionTimerRestarted"), InlineData("PossessionRemember")]
    [InlineData("LockdownConscript"), InlineData("EmergencyExitOpened"), InlineData("EmergencyExitVerdict")]
    public void AttributionEchoContainsFinalTextOnlyAfterTheMouth(string trigger)
    {
        var e = Engine(Rule(trigger, "hello {name}"));
        e.MarkWhisperAudio(10); // WPF permits these authored text-only attribution lines
        string? echo = null;
        e.BarkSpoken += (_, text) => { Assert.Single(_said); echo = text; };
        Assert.True(e.Raise(trigger, c => c.Set("name", "friend")));
        Assert.Equal("hello friend", echo);
        echo = null;
        e.IsSpeaking = () => true;
        Assert.False(e.Raise(trigger));
        Assert.Null(echo);
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
