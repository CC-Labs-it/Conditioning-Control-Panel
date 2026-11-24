using System;
using System.IO;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Services;

/// <summary>
/// The click a quest card makes when it opens its feature (staff ask, 2026-11-18). One soft
/// one-shot, the same <c>chaos/ui_click.mp3</c> the launcher and the friends drawer use, on the
/// <see cref="Friends.FriendsSfx"/> shape: through <c>App.Audio.PlayOneShot</c>, silent when output
/// is suppressed, the master volume is 0, a mandatory video is playing, Emi's hold is up, or the
/// file is missing. Never throws.
///
/// <para>The panel has no UI-sound switch of its own (the launcher's speaker mutes launcher chrome
/// only), so this follows the master volume and stays quieter than the launcher's own click; a
/// session in progress halves it again.</para>
/// </summary>
internal static class QuestSfx
{
    internal const string OpenCue = "chaos/ui_click.mp3";

    /// <summary>Share of the master volume. The launcher click is 0.16, the friends click 0.12.</summary>
    internal const float OpenScale = 0.10f;

    /// <summary>The level a card click plays at, or 0 for silence. Pure, so the rule is pinned.</summary>
    internal static float Level(int masterVolume, bool sessionRunning)
    {
        if (masterVolume <= 0) return 0f;
        var level = Math.Clamp(masterVolume / 100f, 0f, 1f) * OpenScale;
        return sessionRunning ? level * 0.5f : level;
    }

    /// <summary>A quest card was clicked and is about to open its page.</summary>
    public static void Open()
    {
        try
        {
            var audio = App.Audio;
            if (audio == null || audio.IsOutputSuppressed) return;
            if (App.Video?.IsPlaying == true) return;   // the mandatory video owns the room
            if (EmiLineEngine.Instance.HoldActive) return;

            var level = Level(App.Settings?.Current?.MasterVolume ?? 0, App.IsSessionRunning);
            if (level <= 0f) return;

            var path = ModResourceResolver.ResolveAudioPath(OpenCue);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            audio.PlayOneShot(path, level, "quest-open");
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[Quests] open click failed");
        }
    }
}
