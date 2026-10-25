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

    internal static void Tick(DateTime now)
    {
        try
        {
            var level = CoreSettings.Current.MasterVolume;
            if (level <= 0 || CoreAudio.IsOutputSuppressed) return;
            if (!Rules.TryPlay(LeashCue.Tick, now, LeashPunishWindow.Current != null, CoreEngine.Video?.IsPlaying == true)) return;
            const string cue = "chaos/countdown_tick.mp3";
            var path = CoreModArt.AudioOverridePath(cue)
                ?? ContentLocator.Resolve(Path.Combine("Resources", "sounds", cue.Replace('/', Path.DirectorySeparatorChar)));
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            CoreAudio.PlayOneShot(path, Math.Clamp(level / 100f * 0.10f, 0f, 1f), "leash-tick");
        }
        catch (Exception ex) { Serilog.Log.Debug("[Leash] tick failed: {E}", ex.Message); }
    }
}
