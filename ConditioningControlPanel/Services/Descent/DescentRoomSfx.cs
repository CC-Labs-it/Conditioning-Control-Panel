using ConditioningControlPanel.Services.Chaos;

namespace ConditioningControlPanel.Services.Descent;

/// <summary>WPF asset resolver for the shared room cues; playback uses the seeded CoreAudio seam.</summary>
public static class DescentRoomSfx
{
    public static bool ShouldPlay(bool audioEnabled, int masterVolume)
        => DescentRoomAudio.ShouldPlay(audioEnabled, masterVolume);

    public static void PlayFogEntry() => Play(DescentRoomCue.FogEntry);
    public static void PlaySplashOpen() => Play(DescentRoomCue.SplashOpen);
    public static void PlayCrack() => Play(DescentRoomCue.Crack);

    private static void Play(DescentRoomCue cue) => DescentRoomAudio.Play(cue,
        App.DescentCountdown?.AudioEnabled ?? true, App.Settings?.Current?.MasterVolume ?? 0,
        ChaosSfx.ResolvePath);
}
