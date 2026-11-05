using ConditioningControlPanel.Models;
namespace ConditioningControlPanel.Services;
/// <summary>WPF MainWindow/MainWindow.Patreon.cs:2233 ApplyVoiceMute: remember the last nonzero volume,
/// disable whisper playback and avatar voice together, then restore 70 if nothing was remembered.</summary>
public sealed class VoiceAudioState
{
    private int _preMuteMasterVolume = 70;
    public void ApplyMute(AppSettings settings, bool muted)
    {
        if (muted && settings.MasterVolume > 0) _preMuteMasterVolume = settings.MasterVolume;
        settings.MasterVolume = muted ? 0 : _preMuteMasterVolume > 0 ? _preMuteMasterVolume : 70;
        settings.SubAudioEnabled = !muted;
        settings.AvatarMuted = muted;
    }
}
