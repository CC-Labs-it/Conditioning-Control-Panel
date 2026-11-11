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

    internal static void Tick(DateTime now) => Play(LeashCue.Tick, now);

    internal static void Play(LeashCue cue) => Play(cue, DateTime.UtcNow);

    internal static void Play(LeashCue cue, DateTime now)
    {
        var (rel, scale) = cue switch
        {
            LeashCue.Jingle => ("chaos/chain_pop.mp3", 0.22f),
            LeashCue.Snap => ("chaos/collar_save.mp3", 0.28f),
            LeashCue.Sent => ("chaos/chip_pop.mp3", 0.16f),
            LeashCue.Stamp => ("chaos/shield_thunk.mp3", 0.2f),
            LeashCue.Ask => ("chaos/reveal_chime.mp3", 0.18f),
            LeashCue.Refused => ("chaos/ui_unequip.mp3", 0.14f),
            LeashCue.Gift => ("chaos/ui_unlock.mp3", 0.18f),
            LeashCue.Scold => ("chaos/thud.mp3", 0.2f),
            LeashCue.Assigned => ("chaos/cards_in.mp3", 0.16f),
            LeashCue.Done => ("chaos/sin_accept.mp3", 0.18f),
            LeashCue.Denied => ("chaos/ui_denied.mp3", 0.16f),
            LeashCue.Tick => ("chaos/countdown_tick.mp3", 0.10f),
            LeashCue.Cut => ("chaos/snap.mp3", 0.22f),
            LeashCue.CapReached => ("chaos/sink.mp3", 0.14f),
            _ => throw new ArgumentOutOfRangeException(nameof(cue)),
        };
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
        catch (Exception ex) { Serilog.Log.Debug("[Leash] tick failed: {E}", ex.Message); }
    }
}
