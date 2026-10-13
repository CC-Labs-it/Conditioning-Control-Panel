using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Bark;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Pins CCP.Core BarkEngine to WPF BarkService (EvaluateGate :1645, CommitFire :1793,
/// ConditionPass :1379, PickVariant :1867). Each engine gets its own AppSettings: parallel classes
/// swap the shared CoreSettings provider.</summary>
public class BarkEngineTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public void Advance(int ms) => Now = Now.AddMilliseconds(ms);
    }

    private static string Uid(string name) => name + "_" + Guid.NewGuid().ToString("N")[..8];

    private static BarkRule Rule(string id, string trigger, int lines = 1, int priority = 0, int cooldownMs = 0,
        bool repeatable = true, string cls = "normal", double chance = 1.0, Dictionary<string, object>? cond = null) => new()
    {
        Id = id, Trigger = trigger, Priority = priority, CooldownMs = cooldownMs, Repeatable = repeatable,
        ClassRaw = cls, Chance = chance, Conditions = cond,
        VariantPool = Enumerable.Range(1, lines).Select(i => new BarkVariant($"{id} line {i}", $"{id}_{i}.mp3")).ToList(),
    };

    private static (BarkEngine, Clock) Engine(params BarkRule[] rules) => Engine(new AppSettings(), rules);

    private static (BarkEngine, Clock) Engine(AppSettings settings, params BarkRule[] rules)
    {
        var clock = new Clock();
        var e = new BarkEngine(clock, new Random(7)) { Settings = () => settings, SaveSettings = () => { } };
        e.Load(new BarkRuleSet(rules));
        return (e, clock);
    }

    [Fact]
    public void GlobalMinGap_BlocksASecondBarkForSixtySeconds()
    {
        var (e, clock) = Engine(Rule(Uid("a"), "TrigA"), Rule(Uid("b"), "TrigB"));
        Assert.NotNull(e.Decide("TrigA", new BarkContext("TrigA")));
        clock.Advance(BarkEngine.GlobalMinGapMs - 1);
        Assert.Null(e.Decide("TrigB", new BarkContext("TrigB")));
        clock.Advance(1);
        Assert.NotNull(e.Decide("TrigB", new BarkContext("TrigB")));
    }

    [Fact]
    public void GapExemptTrigger_KeepsItsOwnCooldownOnly()
    {
        var (e, clock) = Engine(Rule(Uid("other"), "TrigA"), Rule(Uid("att"), "AttentionCheckFail", cooldownMs: 20000));
        Assert.NotNull(e.Decide("TrigA", new BarkContext("TrigA")));
        Assert.NotNull(e.Decide("AttentionCheckFail", new BarkContext("AttentionCheckFail")));   // min-gap skipped
        clock.Advance(19999);
        Assert.Null(e.Decide("AttentionCheckFail", new BarkContext("AttentionCheckFail")));      // own cooldown
        clock.Advance(1);
        Assert.NotNull(e.Decide("AttentionCheckFail", new BarkContext("AttentionCheckFail")));
    }

    [Fact]
    public void SafetyBark_BypassesTheGapAndHoldsTheFloor()
    {
        var (e, clock) = Engine(Rule(Uid("n"), "TrigA", cooldownMs: 0), Rule(Uid("panic"), "Panic", cls: "safety"),
            Rule(Uid("att"), "AttentionCheckFail"));
        Assert.NotNull(e.Decide("TrigA", new BarkContext("TrigA")));
        Assert.NotNull(e.Decide("Panic", new BarkContext("Panic")));                              // bypasses min-gap
        clock.Advance(BarkEngine.SafetyHoldMs - 1);
        Assert.Null(e.Decide("AttentionCheckFail", new BarkContext("AttentionCheckFail")));      // safety-active
        clock.Advance(1);
        Assert.NotNull(e.Decide("AttentionCheckFail", new BarkContext("AttentionCheckFail")));
    }

    [Fact]
    public void OneShot_FiresOncePerSession_AndAReloadRearmsIt()
    {
        var once = Rule(Uid("egg"), "BouncingTextCorner", repeatable: false, cls: "easter_egg");
        var (e, clock) = Engine(once);
        Assert.NotNull(e.Decide("BouncingTextCorner", new BarkContext("BouncingTextCorner")));
        clock.Advance(BarkEngine.GlobalMinGapMs);
        Assert.Null(e.Decide("BouncingTextCorner", new BarkContext("BouncingTextCorner")));
        e.Reload(new BarkRuleSet(new[] { once }));
        Assert.NotNull(e.Decide("BouncingTextCorner", new BarkContext("BouncingTextCorner")));
    }

    [Fact]
    public void RepeatablePool_NeverRepeatsUntilExhausted_AndRecyclesWithoutAnImmediateRepeat()
    {
        var (e, clock) = Engine(Rule(Uid("pool"), "AttentionCheckFail", lines: 3));
        var seen = new List<string>();
        for (int i = 0; i < 4; i++)
        {
            seen.Add(e.Decide("AttentionCheckFail", new BarkContext("AttentionCheckFail"))!.Variant.Text);
            clock.Advance(1);
        }
        Assert.Equal(3, seen.Take(3).Distinct().Count());
        Assert.NotEqual(seen[2], seen[3]);
    }

    [Fact]
    public void Conditions_TakeTheHighestPriorityRuleWhoseConditionsPass()
    {
        var high = Rule(Uid("high"), "AvatarClicked", priority: 50, cond: new() { ["clicks_60s_gte"] = 5.0 });
        var low = Rule(Uid("low"), "AvatarClicked", priority: 10, cond: new() { ["is_pb"] = true });
        var (e, clock) = Engine(high, low);
        Assert.Null(e.Decide("AvatarClicked", new BarkContext("AvatarClicked").Set("is_pb", false)));
        var pick = e.Decide("AvatarClicked", new BarkContext("AvatarClicked").Set("is_pb", true));
        Assert.Equal(low.Id, pick!.Rule.Id);
        clock.Advance(BarkEngine.GlobalMinGapMs);
        for (int i = 0; i < 5; i++) e.RegisterAvatarClick();
        Assert.Equal(high.Id, e.Decide("AvatarClicked", new BarkContext("AvatarClicked"))!.Rule.Id);
    }

    [Fact]
    public void CompanionOff_SilencesEverythingButSafety()
    {
        var (e, _) = Engine(new AppSettings { AvatarEnabled = false }, Rule(Uid("n"), "TrigA"), Rule(Uid("panic"), "Panic", cls: "safety"));
        Assert.Null(e.Decide("TrigA", new BarkContext("TrigA")));
        Assert.NotNull(e.Decide("Panic", new BarkContext("Panic")));

        var (emi, _) = Engine(Rule(Uid("n"), "TrigA"), Rule(Uid("panic"), "Panic", cls: "safety"));
        emi.EmiAvatarMuted = () => true;   // EMI Desk muted avatar: same exemption
        Assert.Null(emi.Decide("TrigA", new BarkContext("TrigA")));
        Assert.NotNull(emi.Decide("Panic", new BarkContext("Panic")));
    }

    [Fact]
    public void WhileSpeaking_AnOrdinaryBarkIsDroppedButAPriorityOnePreempts()
    {
        var (e, _) = Engine(Rule(Uid("n"), "TrigA"), Rule(Uid("p"), "TrigB", priority: BarkEngine.PriorityBarkThreshold));
        e.AvatarSpeaking = () => true;
        Assert.Null(e.Decide("TrigA", new BarkContext("TrigA")));
        var pick = e.Decide("TrigB", new BarkContext("TrigB"));
        Assert.True(pick!.Preempts);
    }

    [Fact]
    public void ChanceZero_NeverFires_AndUserChatSuppresses()
    {
        var (e, clock) = Engine(Rule(Uid("c"), "TrigA", chance: 0.0), Rule(Uid("d"), "TrigB"));
        Assert.Null(e.Decide("TrigA", new BarkContext("TrigA")));
        e.NoteUserMessage();
        Assert.Null(e.Decide("TrigB", new BarkContext("TrigB")));
        clock.Advance(new AppSettings().BarkChatSuppressionMs);
        Assert.NotNull(e.Decide("TrigB", new BarkContext("TrigB")));
    }

    [Fact]
    public void Substitutions_FillContextTokensAndTheFocusedApp()
    {
        var ctx = new BarkContext("ChaosRankUp").Set("rank", "doll");
        Assert.Equal("a doll now, in that", BarkEngine.ApplySubstitutions("a {rank} now, in {0}", ctx));
    }
}
