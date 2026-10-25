using System;
using System.Linq;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.Bark;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Services.Chaos;
using Xunit;
namespace CCP.Core.Tests;
[Collection(SessionStatics.Name)]
public sealed class CompanionIdleHostTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Utc = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Utc;
    }
    [Fact]
    public void IdleRotationAndGates()
    {
        var s = CoreSettings.Current;
        var old = (s.AvatarEnabled, s.MasterVolume, s.BarkChatSuppressionMs);
        try
        {
            (s.AvatarEnabled, s.MasterVolume, s.BarkChatSuppressionMs) = (true, 50, 0);
            var rules = Enumerable.Range(0, 3).Select(i => new BarkRule {
                Id = Guid.NewGuid().ToString(), Trigger = "Idle",
                VariantPool = new() { new BarkVariant(i.ToString()) }
            }).ToArray();
            var said = new System.Collections.Generic.List<BarkSpeech>();
            var clock = new Clock();
            var e = new BarkEngine(() => new BarkRuleSet(rules), new Random(7)) { Speak = said.Add, Clock = clock };
            e.Start(); e.IsSpeaking = () => true;
            Assert.False(e.DispatchIdle()); e.IsSpeaking = () => false;
            for (int i = 0; i < 3; i++)
            {
                Assert.True(e.DispatchIdle()); Assert.False(e.DispatchIdle());
                clock.Utc += TimeSpan.FromSeconds(61);
            }
            Assert.Equal(3, said.Select(x => x.Text).Distinct().Count());
            Assert.All(said, x => Assert.True(x.Unprompted));
            var last = said.Last().Text;
            Assert.True(e.DispatchIdle()); Assert.NotEqual(last, said.Last().Text);
            s.MasterVolume = 0; clock.Utc += TimeSpan.FromSeconds(61); Assert.False(e.DispatchIdle());
        }
        finally { (s.AvatarEnabled, s.MasterVolume, s.BarkChatSuppressionMs) = old; }
    }
    [Theory]
    [InlineData(true, false, false, false, false, false, false)]
    [InlineData(true, false, false, false, true, false, true)]
    [InlineData(true, true, false, false, false, false, true)]
    [InlineData(true, false, true, false, false, false, true)]
    [InlineData(true, false, true, true, false, false, false)]
    [InlineData(true, true, true, false, true, true, false)]
    [InlineData(false, false, false, false, false, true, true)]
    public void VoiceGate(bool idle, bool engine, bool session, bool paused, bool optIn, bool panic, bool expected)
        => Assert.Equal(expected, CompanionIdleVoiceRule.ShouldVoice(idle, engine, session, paused, optIn, panic));
    [Fact]
    public void VoiceTelemetryCountsOnlyActiveRunAndResets()
    {
        var v = new DtrhVoiceTelemetry();
        v.NoteVoicelineHeard(9); Assert.Equal(0, v.VoicelinesHeard);
        v.StartRun(); v.NoteVoicelineHeard(0); v.NoteVoicelineHeard(2.5); v.NoteVoicelineHeard(-1);
        Assert.Equal(3, v.VoicelinesHeard); Assert.Equal(2.5, v.VoiceoverSec);
        v.EndRun(); v.NoteVoicelineHeard(3); Assert.Equal(3, v.VoicelinesHeard);
        v.StartRun(); Assert.Equal(0, v.VoicelinesHeard); Assert.Equal(0, v.VoiceoverSec);
    }
}
