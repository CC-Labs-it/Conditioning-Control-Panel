using System;
using Serilog;

namespace ConditioningControlPanel.Services.Descent;

public enum DescentRoomCue { FogEntry, SplashOpen, Crack }

/// <summary>The room's WPF cue ladders and mix, shared across heads. Asset resolution stays in the head.</summary>
public static class DescentRoomAudio
{
    private static readonly string[] FogEntry = { "descent_fog_enter", "ui_deepen" };
    private static readonly string[] SplashOpen = { "descent_spiral_open", "reveal_chime" };
    private static readonly string[] Crack = { "descent_crack", "descent_zero_flash", "freeze_trigger" };
    private static bool _missingLogged;

    public static bool ShouldPlay(bool audioEnabled, int masterVolume) => audioEnabled && masterVolume > 0;

    /// <summary>Dedicated asset first, shipped fallback second; muted, absent or broken audio stays silent.</summary>
    public static void Play(DescentRoomCue cue, bool audioEnabled, int masterVolume, Func<string, string> resolve)
    {
        if (!ShouldPlay(audioEnabled, masterVolume)) return;
        var (candidates, scale, tag) = cue switch
        {
            DescentRoomCue.FogEntry => (FogEntry, 0.42f, "descent-fog-enter"),
            DescentRoomCue.SplashOpen => (SplashOpen, 0.34f, "descent-spiral-open"),
            DescentRoomCue.Crack => (Crack, 0.55f, "descent-crack"),
            _ => throw new ArgumentOutOfRangeException(nameof(cue)),
        };
        try
        {
            foreach (var name in candidates)
            {
                var path = resolve(name);
                if (string.IsNullOrEmpty(path)) continue;
                CoreAudio.PlayOneShot(path, Math.Clamp(masterVolume / 100f * scale, 0f, 1f), tag);
                return;
            }
            if (_missingLogged) return;
            _missingLogged = true;
            Log.Debug("[Spiral] No sound for {Tag} ({Candidates}) - the room stays quiet.", tag, string.Join(", ", candidates));
        }
        catch (Exception ex) { Log.Debug("[Spiral] sfx {Tag} failed: {E}", tag, ex.Message); }
    }
}
