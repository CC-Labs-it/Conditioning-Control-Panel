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

    [Theory] // WPF BarkService.cs:197-200: only the exact value 1 enables the mode at startup.
    [InlineData("1", true)]
    [InlineData("true", false)]
    [InlineData("0", false)]
    public void StartupReadsDryRunOptIn(string value, bool expected)
    {
        var old = Environment.GetEnvironmentVariable("CCP_BARK_DRYRUN");
        try
        {
            Environment.SetEnvironmentVariable("CCP_BARK_DRYRUN", value);
            Assert.Equal(expected, Engine().DryRun);
        }
        finally { Environment.SetEnvironmentVariable("CCP_BARK_DRYRUN", old); }
    }

    [Fact] // WPF BarkService.cs:1159-1166, 1772-1821, 1900-1907, 2006-2029.
    public void DryRunLogsAndAdvancesTheSessionWithoutSpeakingOrPersisting()
    {
        var once = Rule("AttentionCheckFail", "private once");
        (once.Repeatable, once.ScopeRaw) = (false, "lifetime");
        var repeat = Rule("PossessionEffect", "private a", "private b");
        var oldLogger = Serilog.Log.Logger;
        var sink = new BarkLogSink();
        Serilog.Log.Logger = new Serilog.LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(sink).CreateLogger();
        try
        {
            // Seed a fully used pool to exercise the recycle path as well as the commit path.
            var oldRotation = repeat.VariantPool!.Select(v => BarkEngine.BarkLineId(repeat.Id, v)).ToList();
            _s.BarkVariantRotation[repeat.Id] = new List<string>(oldRotation);
            var e = new BarkEngine(() => new BarkRuleSet(new[] { once, repeat }), new ZeroRandom())
                { Speak = _said.Add, DryRun = true };
            e.Start();
            int echoes = 0;
            e.BarkSpoken += (_, _) => echoes++;
            Assert.False(e.Raise("AttentionCheckFail"));
            Assert.False(e.Raise("AttentionCheckFail", guaranteed: true)); // session latch still applies
            e.Speak = null; // WPF dry-run also decides when no avatar mouth exists
            Assert.False(e.Raise("PossessionEffect"));
            Assert.False(e.Raise("PossessionEffect"));
            Assert.Empty(_said);
            Assert.Equal(0, echoes);
            Assert.False(_s.IsBarkFired(once.Id));
            Assert.False(_s.BarkVariantRotation.ContainsKey(once.Id));
            Assert.Equal(oldRotation, _s.BarkVariantRotation[repeat.Id]);
            var messages = sink.Events.Select(x => x.RenderMessage())
                .Where(m => m.Contains(once.Id) || m.Contains(repeat.Id)).ToList();
            Assert.Contains(messages, m => m.Contains("[BARK dry-run] WOULD FIRE") && m.Contains(once.Id));
            Assert.Contains(messages, m => m.Contains("[BARK dry-run] blocked") && m.Contains("already-fired"));
            var picks = messages.Where(m => m.Contains("WOULD FIRE") && m.Contains(repeat.Id)).ToList();
            Assert.Equal(2, picks.Count);
            Assert.NotEqual(picks[0].Split("variant#=")[1], picks[1].Split("variant#=")[1]);
            Assert.DoesNotContain(messages, m => m.Contains("private"));
            Assert.True(Engine(once).Raise("AttentionCheckFail")); // no lifetime latch survived the dry run
        }
        finally
        {
            Serilog.Log.Logger = oldLogger;
            _s.BarkVariantRotation.Remove(once.Id);
            _s.BarkVariantRotation.Remove(repeat.Id);
            _s.BarkLifetimeFired.Remove(once.Id);
        }
    }

    private sealed class ZeroRandom : Random
    {
        public override int Next(int maxValue) => 0;
    }

    private sealed class BarkLogSink : Serilog.Core.ILogEventSink
    {
        public System.Collections.Concurrent.ConcurrentQueue<Serilog.Events.LogEvent> Events { get; } = new();
        public void Emit(Serilog.Events.LogEvent e) => Events.Enqueue(e);
    }

    [Fact] // WPF AudioService.cs:948 and BarkService.cs:1664-1670.
    public void WhisperHoldsOrdinaryBarksUntilTheLongestTailButNotSafetyGuaranteedOrAttribution()
    {
        var clock = new PopQuizSchedulerTests.FakeClock();
        var normal = Rule("AttentionCheckFail", "ordinary");
        var attribution = Rule("PossessionEffect", "text");
        var panic = Rule("Panic", "safe");
        panic.ClassRaw = "safety";
        var e = Engine(normal, attribution, panic);
        e.Clock = clock;
        e.MarkWhisperAudio(4);
        e.MarkWhisperAudio(1);
        Assert.False(e.Raise("AttentionCheckFail"));
        Assert.True(e.Raise("PossessionEffect"));
        Assert.True(e.Raise("AttentionCheckFail", guaranteed: true));
        Assert.True(e.Raise("Panic"));

        e = Engine(normal); e.Clock = clock;
        e.MarkWhisperAudio(4); e.MarkWhisperAudio(1);
        clock.Advance(TimeSpan.FromSeconds(4.1));
        Assert.False(e.Raise("AttentionCheckFail"));
        clock.Advance(TimeSpan.FromSeconds(0.2));
        Assert.True(e.Raise("AttentionCheckFail"));
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
