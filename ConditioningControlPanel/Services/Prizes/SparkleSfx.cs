using System;
using System.IO;

namespace ConditioningControlPanel.Services.Prizes;

/// <summary>
/// The flight's sounds: soft bells in A major pentatonic, a settle chord and a breath of air,
/// rendered offline by <c>Scripts/render-sparkle-bells.mjs</c> into <c>Resources/sounds/sparkles/</c>.
/// One-shots with a per-sound sample gain (<c>AudioFileReader.Volume</c> inside
/// <c>App.Audio.PlayOneShot</c>, never the device volume). Muted by the launcher's speaker button
/// like every launcher cue, and by master volume 0. The files peak at most 0.9 and play at the
/// launcher click's scale or below, so the loudest moment (the chord) is never louder than chrome.
/// </summary>
internal static class SparkleSfx
{
    private const float BellScale = 0.16f;
    private const float ChordScale = 0.16f;
    private const float AirScale = 0.16f;

    public static void Bell(int rung) => Play($"sparkles/bell_{Math.Clamp(rung, 0, SparkleFlightPlan.Bells - 1):00}.wav", BellScale, "sparkle-bell");

    public static void Chord() => Play("sparkles/chord.wav", ChordScale, "sparkle-chord");

    public static void Air(bool up) => Play(up ? "sparkles/air_up.wav" : "sparkles/air_down.wav", AirScale, "sparkle-air");

    private static readonly System.Collections.Generic.List<AudioPlaybackHandle> Live = new();

    /// <summary>Panic: every flight sound still playing stops now.</summary>
    public static void StopAll()
    {
        AudioPlaybackHandle[] live;
        lock (Live) { live = Live.ToArray(); Live.Clear(); }
        foreach (var h in live)
        {
            try { h.Stop(); } catch (Exception ex) { App.Logger?.Debug("Sparkle sfx stop: {E}", ex.Message); }
        }
    }

    private static void Play(string rel, float scale, string tag)
    {
        try
        {
            var audio = App.Audio;
            var s = App.Settings?.Current;
            if (audio == null || audio.IsOutputSuppressed || s == null) return;
            if (!s.LauncherSoundEnabled) return;
            var master = Math.Clamp(s.MasterVolume / 100f, 0f, 1f);
            if (master <= 0f) return;
            var path = ModResourceResolver.ResolveAudioPath(rel);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            if (audio.PlayOneShot(path, Math.Clamp(master * scale, 0f, 1f), tag) is not { } handle) return;
            lock (Live) { Live.RemoveAll(h => h.IsFinished); Live.Add(handle); }
        }
        catch (Exception ex) { App.Logger?.Debug("Sparkle sfx {Tag}: {E}", tag, ex.Message); }
    }
}
