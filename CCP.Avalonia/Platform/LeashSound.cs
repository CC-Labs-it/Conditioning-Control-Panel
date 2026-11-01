// WPF Controls/Leash/LeashFx.cs Tick/Cut/Play: reuse the Core gate and the head's one-shot player.
using System;
using System.IO;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Leash;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;

namespace ConditioningControlPanel.Avalonia.Platform;

internal static class LeashSound
{
    private static readonly LeashSfxRules Rules = new();

    internal static void Tick(DateTime now) => Play(LeashCue.Tick, "chaos/countdown_tick.mp3", 0.10f, now);

    internal static void Cut(DateTime now) => Play(LeashCue.Cut, "chaos/snap.mp3", 0.22f, now);

    private static void Play(LeashCue cue, string rel, float scale, DateTime now)
    {
        try
        {
            var level = CoreSettings.Current.MasterVolume;
            if (level <= 0 || CoreAudio.IsOutputSuppressed) return;
            if (!Rules.TryPlay(cue, now, LeashPunishWindow.Current != null, CoreEngine.Video?.IsPlaying == true)) return;
            var path = CoreModArt.AudioOverridePath(rel)
                ?? ContentLocator.Resolve(Path.Combine("Resources", "sounds", rel.Replace('/', Path.DirectorySeparatorChar)));
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            CoreAudio.PlayOneShot(path, Math.Clamp(level / 100f * scale, 0f, 1f), "leash-" + cue.ToString().ToLowerInvariant());
        }
        catch (Exception ex) { Serilog.Log.Debug("[Leash] sound failed: {E}", ex.Message); }
    }
}
