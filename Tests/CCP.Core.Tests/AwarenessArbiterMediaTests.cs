using System;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Awareness;
using Xunit;

namespace CCP.Core.Tests;

public class AwarenessArbiterMediaTests
{
    private sealed class Speaker : IAwarenessSpeaker
    {
        public int Calls;
        public string? CurrentAppId => null;
        public bool TrySpeakBark(ContextFrame frame) { Calls++; return true; }
        public bool TrySpeakLine(string line, RarityTier tier) { Calls++; return true; }
    }

    [Fact]
    public async Task DisabledServiceNeverResolvesTransport()
    {
        var calls = 0;
        var service = new AwarenessReactionService(() => { calls++; return null; }, () => false);
        var result = await service.GetAwarenessReactionAsync(new ContextFrame());
        Assert.Equal("v2-off", result.Reason);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task MediaLoopDeliversOnceAndBurnsOneCooldown()
    {
        var now = new DateTime(2026, 8, 3, 9, 0, 0);
        var cooldowns = new ReactionCooldownLedger(() => AwarenessIntensity.Chatty);
        var speaker = new Speaker();
        var arbiter = new ReactionArbiter(cooldowns, localClock: () => now, speaker: speaker);
        var frame = new ContextFrame
        {
            AppId = "player", Transition = TransitionKind.MediaChanged, Tier = RarityTier.Common,
            Trends = new[] { new TrendEvent(TrendKind.MediaLoop, "player", null, 3, 0, 0, 0, null) }
        };

        Assert.Equal(AwarenessVerdict.Bark, (await arbiter.SubmitAsync(frame)).Verdict);
        Assert.Equal(AwarenessVerdict.Silence, (await arbiter.SubmitAsync(frame)).Verdict);
        Assert.Equal(1, speaker.Calls);
        Assert.Equal(1, cooldowns.LinesLastHour(now));
    }
}
