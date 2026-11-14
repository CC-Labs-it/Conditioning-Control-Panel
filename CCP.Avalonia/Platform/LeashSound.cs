// WPF Controls/Leash/LeashFx.cs Tick/Snap/Ask/Denied/Cut and the event cues of LeashSurfaces.OnEvent
// (:127-160): reuse the Core gate and the head's one-shot player.
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

    internal static void Snap(DateTime now) => Play(LeashCue.Snap, "chaos/collar_save.mp3", 0.28f, now);

    // WPF LeashFx.Ask/Denied (LeashFx.cs:204,222).
    internal static void Ask(DateTime now) => Play(LeashCue.Ask, "chaos/reveal_chime.mp3", 0.18f, now);

    internal static void Denied(DateTime now) => Play(LeashCue.Denied, "chaos/ui_denied.mp3", 0.16f, now);

    internal static void Cut(DateTime now) => Play(LeashCue.Cut, "chaos/snap.mp3", 0.22f, now);

    /// <summary>WPF LeashSurfaces.OnEvent: the cue each landed event plays (an accepted answer plays the snap instead).</summary>
    internal static void Event(LeashEventKind kind, DateTime now)
    {
        var cue = kind switch
        {
            LeashEventKind.Tug => (LeashCue.Jingle, "chaos/chain_pop.mp3", 0.22f),
            LeashEventKind.Answered or LeashEventKind.PunishSkipped => (LeashCue.Refused, "chaos/ui_unequip.mp3", 0.14f),
            LeashEventKind.Ended => (LeashCue.Cut, "chaos/snap.mp3", 0.22f),
            LeashEventKind.Reward => (LeashCue.Gift, "chaos/ui_unlock.mp3", 0.18f),
            LeashEventKind.Punish or LeashEventKind.AssignMissed => (LeashCue.Scold, "chaos/thud.mp3", 0.2f),
            LeashEventKind.Assign => (LeashCue.Assigned, "chaos/cards_in.mp3", 0.16f),
            LeashEventKind.AssignDone or LeashEventKind.PunishDone => (LeashCue.Done, "chaos/sin_accept.mp3", 0.18f),
            _ => ((LeashCue, string, float)?)null,
        };
        if (cue is { } c) Play(c.Item1, c.Item2, c.Item3, now);
    }

    private static void Play(LeashCue kind, string cue, float scale, DateTime now)
    {
        try
        {
            var level = CoreSettings.Current.MasterVolume;
            if (level <= 0 || CoreAudio.IsOutputSuppressed) return;
            if (!Rules.TryPlay(kind, now, LeashPunishWindow.Current != null, CoreEngine.Video?.IsPlaying == true)) return;
            var path = CoreModArt.AudioOverridePath(cue)
                ?? ContentLocator.Resolve(Path.Combine("Resources", "sounds", cue.Replace('/', Path.DirectorySeparatorChar)));
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            CoreAudio.PlayOneShot(path, Math.Clamp(level / 100f * scale, 0f, 1f), "leash-" + kind.ToString().ToLowerInvariant());
        }
        catch (Exception ex) { Serilog.Log.Debug("[Leash] {Cue} sound failed: {E}", kind, ex.Message); }
    }
}
