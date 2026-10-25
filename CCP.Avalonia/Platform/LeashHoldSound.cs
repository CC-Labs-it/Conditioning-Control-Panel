using System;
using System.IO;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Leash;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF LeashFx.Tick: the countdown cue, with the shared leash sound rules.</summary>
internal static class LeashHoldSound
{
    /// <summary>The ONE leash sound-rule instance for this head: WPF LeashFx shares a single 130 ms gap across every
    /// leash cue, so any later leash cue must call TryPlay on this, never on its own LeashSfxRules.</summary>
    internal static readonly LeashSfxRules Rules = new();

    internal static void Tick()
    {
        try
        {
            var level = CoreSettings.Current.MasterVolume;
            if (level <= 0 || CoreAudio.IsOutputSuppressed) return;
            if (!Rules.TryPlay(LeashCue.Tick, DateTime.UtcNow,
                LeashPunishWindow.Current != null, CoreEngine.Video?.IsPlaying == true)) return;
            const string rel = "chaos/countdown_tick.mp3";
            var path = CoreModArt.AudioOverridePath(rel)
                ?? ContentLocator.Resolve(Path.Combine("Resources", "sounds", rel));
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            CoreAudio.PlayOneShot(path, Math.Clamp(level / 100f * 0.10f, 0f, 1f), "leash-tick");
        }
        catch (Exception ex) { Serilog.Log.Debug(ex, "Leash hold tick failed"); }
    }
}
