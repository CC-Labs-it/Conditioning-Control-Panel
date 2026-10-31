using System.Linq;
using System.Reflection;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Services.Safety;
using Xunit;

namespace CCP.Core.Tests;

[Collection(SessionStatics.Name)]
public class CompanionIdleVoiceTests
{
    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, true, true)]
    [InlineData(false, false, false, true)]
    public void VoiceRule(bool idle, bool running, bool optedIn, bool expected) =>
        Assert.Equal(expected, CompanionIdleVoice.ShouldVoice(idle, running, optedIn));

    [Theory]
    [InlineData("Idle")]
    [InlineData("idlestatechanged")]
    [InlineData("FaceLost")]
    [InlineData("FaceFound")]
    [InlineData("Blink")]
    [InlineData("TrackingStateChanged")]
    [InlineData("ActivityChanged")]
    [InlineData("StillOnActivity")]
    public void SensingTriggersAreUnprompted(string trigger) =>
        Assert.True(CompanionIdleVoice.IsUnprompted(trigger));

    [Theory]
    [InlineData(null)]
    [InlineData("UserMessageSent")]
    [InlineData("AvatarClicked")]
    public void PlayerActionsArePrompted(string? trigger) =>
        Assert.False(CompanionIdleVoice.IsUnprompted(trigger));

    [Theory]
    [InlineData(false, false, true, false)] // paused session, no opt-in
    [InlineData(false, false, false, true)] // live session
    [InlineData(false, true, true, true)]   // paused session, opted in
    [InlineData(true, true, false, false)]  // panic overrides opt-in and a live session
    [InlineData(true, true, true, false)]   // panic overrides opt-in and a paused session
    public void LiveGateHonorsPauseAndPanic(bool panic, bool optedIn, bool paused, bool expected)
    {
        var engine = CoreSession.IsEngineRunningProvider;
        var session = CoreSession.IsSessionRunningProvider;
        var pause = CoreSession.IsSessionPausedProvider;
        var opt = CoreSettings.Current.CompanionSpeaksIdleWhenEngineOff;
        var panicState = typeof(PanicAftermath).GetFields(BindingFlags.Static | BindingFlags.NonPublic)
            .Where(f => !f.IsInitOnly).Select(f => (Field: f, Value: f.GetValue(null))).ToArray();
        try
        {
            CoreSession.IsEngineRunningProvider = () => false;
            CoreSession.IsSessionRunningProvider = () => true;
            CoreSession.IsSessionPausedProvider = () => paused;
            CoreSettings.Current.CompanionSpeaksIdleWhenEngineOff = optedIn;
            PanicAftermath.EndQuiet();
            if (panic) PanicAftermath.ArmQuiet();
            Assert.Equal(expected, CompanionIdleVoice.ShouldVoiceNow(true));
            Assert.True(CompanionIdleVoice.ShouldVoiceNow(false));
            if (panic)
            {
                CoreSession.IsEngineRunningProvider = () => true;
                Assert.False(CompanionIdleVoice.ShouldVoiceNow(true));
            }
        }
        finally
        {
            foreach (var (field, value) in panicState) field.SetValue(null, value);
            CoreSession.IsEngineRunningProvider = engine;
            CoreSession.IsSessionRunningProvider = session;
            CoreSession.IsSessionPausedProvider = pause;
            CoreSettings.Current.CompanionSpeaksIdleWhenEngineOff = opt;
        }
    }
}
