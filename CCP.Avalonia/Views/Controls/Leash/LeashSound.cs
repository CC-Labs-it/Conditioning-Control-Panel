using System;
using System.IO;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;

/// <summary>WPF LeashFx.Tick, using the head's existing one-shot player.</summary>
internal static class LeashSound
{
    private static readonly LeashSfxRules Rules = new();
    internal static TimeProvider Clock = TimeProvider.System;

    public static void Tick() => Play(LeashCue.Tick, "chaos/countdown_tick.mp3", 0.10f);
    public static void Cut() => Play(LeashCue.Cut, "chaos/snap.mp3", 0.22f);
    public static void Denied() => Play(LeashCue.Denied, "chaos/ui_denied.mp3", 0.16f);

    private static void Play(LeashCue cue, string rel, float scale)
    {
        try
        {
            var level = CoreSettings.Current.MasterVolume;
            if (CoreAudio.IsOutputSuppressed || level <= 0) return;
            if (!Rules.TryPlay(cue, Clock.GetUtcNow().UtcDateTime, LeashPunishWindow.Current != null,
                MandatoryVideoOverlay.Instance.Scheduler.IsPlaying)) return;
            var path = CoreModArt.AudioOverridePath(rel) ??
                ContentLocator.Resolve(Path.Combine("Resources", "sounds", rel));
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            CoreAudio.PlayOneShot(path, Math.Clamp(level / 100f * scale, 0f, 1f), "leash-" + cue.ToString().ToLowerInvariant());
        }
        catch (Exception ex) { Serilog.Log.Debug("Leash tick failed: {E}", ex.Message); }
    }
}
