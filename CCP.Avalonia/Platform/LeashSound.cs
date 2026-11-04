// WPF Controls/Leash/LeashFx.cs Tick/Play: reuse the Core gate and the head's one-shot player.
using System;
using System.IO;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Leash;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;

namespace ConditioningControlPanel.Avalonia.Platform;

internal static class LeashSound
{
    private static readonly LeashSfxRules Rules = new();

    internal static void Tick(DateTime now) => Play(LeashCue.Tick, now, "chaos/countdown_tick.mp3", 0.10f, "leash-tick");

    internal static void Snap() => Play(LeashCue.Snap, DateTime.UtcNow, "chaos/collar_save.mp3", 0.28f, "leash-snap");

    internal static void Release(bool ok) => Play(ok ? LeashCue.Cut : LeashCue.Denied, DateTime.UtcNow,
        ok ? "chaos/snap.mp3" : "chaos/ui_denied.mp3", ok ? 0.22f : 0.16f, ok ? "leash-cut" : "leash-denied");

    private static void Play(LeashCue kind, DateTime now, string cue, float gain, string tag)
    {
        try
        {
            var level = CoreSettings.Current.MasterVolume;
            if (level <= 0 || CoreAudio.IsOutputSuppressed) return;
            if (!Rules.TryPlay(kind, now, LeashPunishWindow.Current != null, CoreEngine.Video?.IsPlaying == true)) return;
            var path = CoreModArt.AudioOverridePath(cue)
                ?? ContentLocator.Resolve(Path.Combine("Resources", "sounds", cue.Replace('/', Path.DirectorySeparatorChar)));
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            CoreAudio.PlayOneShot(path, Math.Clamp(level / 100f * gain, 0f, 1f), tag);
        }
        catch (Exception ex) { Serilog.Log.Debug("[Leash] tick failed: {E}", ex.Message); }
    }
}
