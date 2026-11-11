using System;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Awareness;
using Xunit;

namespace CCP.Core.Tests.Awareness;

public class ReactionArbiterCoreTests
{
    private sealed class Speaker : IAwarenessSpeaker
    {
        public int Deliveries;
        public string? CurrentAppId => null;
        public bool TrySpeakBark(ContextFrame frame) { Deliveries++; return true; }
        public bool TrySpeakLine(string line, RarityTier tier) { Deliveries++; return true; }
    }

    [Fact]
    public async Task CooldownAllowsOneDeliveryAndOneScorerRegistration()
    {
        var now = new DateTime(2026, 8, 7, 9, 0, 0, DateTimeKind.Local);
        var scorer = new WorthinessScorer(() => AwarenessIntensity.Chatty);
        var speaker = new Speaker();
        var arbiter = new ReactionArbiter(new ReactionCooldownLedger(() => AwarenessIntensity.Chatty),
            scorer, () => now, speaker);
        var frame = new ContextFrame { AppId = "example", Tier = RarityTier.Common };
        var before = scorer.CurrentThreshold(now);

        var first = await arbiter.SubmitAsync(frame);
        var second = await arbiter.SubmitAsync(frame);

        Assert.Equal(AwarenessVerdict.Bark, first.Verdict);
        Assert.Equal(AwarenessVerdict.Silence, second.Verdict);
        Assert.Equal(1, speaker.Deliveries);
        Assert.Equal(before + WorthinessScorer.ThresholdBump, scorer.CurrentThreshold(now), 8);
        Assert.Equal(1, arbiter.Cooldowns.LinesLastHour(now));
    }
}
