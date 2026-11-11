using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
namespace CCP.Core.Tests;
public sealed class VoiceAudioStateTests
{
    [Fact]
    public void VoiceMuteRemembersNonzeroVolumeAndRepeatedMuteCannotOverwriteIt()
    {
        var settings = new AppSettings();
        settings.MasterVolume = 43;
        var audio = new VoiceAudioState();
        audio.ApplyMute(settings, true);
        audio.ApplyMute(settings, true);
        Assert.Equal(0, settings.MasterVolume); Assert.False(settings.SubAudioEnabled); Assert.True(settings.AvatarMuted);
        audio.ApplyMute(settings, false);
        Assert.Equal(43, settings.MasterVolume); Assert.True(settings.SubAudioEnabled); Assert.False(settings.AvatarMuted);
        new VoiceAudioState().ApplyMute(settings, false);
        Assert.Equal(70, settings.MasterVolume);
    }
}
