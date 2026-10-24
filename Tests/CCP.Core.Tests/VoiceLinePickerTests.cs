using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services.Bark;
using Xunit;

namespace CCP.Core.Tests;

public sealed class VoiceLinePickerTests
{
    private static BarkRule Rule(string id, params BarkVariant[] variants) => new()
    { Id = id, Trigger = "VoiceCommand", VariantPool = variants.ToList() };

    [Fact]
    public void TextAndAudioStayPairedAndEachRuleAvoidsAnImmediateRepeat()
    {
        var picker = new VoiceLinePicker(new Random(7));
        var rules = new BarkRuleSet(new[]
        {
            Rule("wake", new BarkVariant("first", "first.wav"), new BarkVariant("second", "second.wav")),
            Rule("other", new BarkVariant("other", null)),
        });
        string? previous = null;
        for (var i = 0; i < 20; i++)
        {
            var line = picker.Pick(rules, i % 2 == 0 ? "WAKE" : "wake", file => "/mod/" + file)!.Value;
            Assert.Equal("/mod/" + line.Text + ".wav", line.Audio);
            Assert.NotEqual(previous, line.Text);
            previous = line.Text;
            Assert.Equal(("other", (string?)null), picker.Pick(rules, "other", _ => null)!.Value);
        }
    }

    [Fact]
    public void MissingRuleOrPoolFallsBackAndReplacementRulesAreReadImmediately()
    {
        var picker = new VoiceLinePicker(new Random(7));
        var oldRules = new BarkRuleSet(new[] { Rule("wake", new BarkVariant("old", "old.wav")) });
        Assert.Null(picker.Pick(oldRules, null, _ => throw new Exception("must not resolve")));
        Assert.Null(picker.Pick(oldRules, "missing", _ => throw new Exception("must not resolve")));
        Assert.Null(picker.Pick(new BarkRuleSet(new[] { new BarkRule { Id = "empty", Trigger = "VoiceCommand", PoolRef = "unused" } }), "empty", _ => null));
        Assert.Equal(("old", (string?)null), picker.Pick(oldRules, "wake", _ => null)!.Value);
        var fresh = new BarkRuleSet(new[] { Rule("wake", new BarkVariant("new", "new.wav")) });
        Assert.Equal(("new", "/new/new.wav"), picker.Pick(fresh, "wake", f => "/new/" + f)!.Value);
    }

    [Fact]
    public void BarkEngineReadsItsReloadedRulesWithoutFiringABark()
    {
        var rules = new BarkRuleSet(new[] { Rule("wake", new BarkVariant("before", "before.wav")) });
        var engine = new BarkEngine(() => rules) { ResolveAudio = f => "/mod/" + f, Speak = _ => throw new Exception("selection must not speak") };
        engine.Start();
        Assert.Equal(("before", "/mod/before.wav"), engine.PickVoiceLine("wake")!.Value);
        rules = new BarkRuleSet(new[] { Rule("wake", new BarkVariant("after", "after.wav")) });
        engine.ReloadRules();
        Assert.Equal(("after", "/mod/after.wav"), engine.PickVoiceLine("wake")!.Value);
    }
}
