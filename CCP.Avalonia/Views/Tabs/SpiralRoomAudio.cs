using System;
using System.IO;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Descent;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

// WPF DescentRoomSfx: same gates, candidate order, mix and failure isolation.
// ponytail: only the two cues used by this room; the crack belongs to the unported zero show.
internal static class SpiralRoomAudio
{
    internal static Func<string, string?> Resolve = ResolveCue;
    private static bool _missingLogged;
    internal static void PlayFogEntry() => Play("descent_fog_enter", "ui_deepen", 0.42f, "descent-fog-enter");
    internal static void PlaySplashOpen() => Play("descent_spiral_open", "reveal_chime", 0.34f, "descent-spiral-open");

    private static string? ResolveCue(string name)
    {
        var relative = $"chaos/{name}.mp3";
        var path = CoreModArt.AudioOverridePath(relative);
        if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path;
        path = ContentLocator.Resolve(Path.Combine("Resources", "sounds", "chaos", name + ".mp3"));
        return File.Exists(path) ? path : null;
    }

    private static void Play(string dedicated, string fallback, float scale, string tag)
    {
        try
        {
            var master = CoreSettings.Current.MasterVolume;
            if (!(App.DescentCountdown?.AudioEnabled ?? true) || master <= 0) return;
            var path = Resolve(dedicated) ?? Resolve(fallback);
            if (string.IsNullOrEmpty(path))
            {
                // WPF: ONE line for the life of the process; a missing asset is a silent no-op.
                if (_missingLogged) return;
                _missingLogged = true;
                Log.Debug("[Spiral] No sound for {Tag} ({Candidates}) - the room stays quiet.",
                    tag, string.Join(", ", dedicated, fallback));
                return;
            }
            CoreAudio.PlayOneShot(path, Math.Clamp(master / 100f * scale, 0f, 1f), tag);
        }
        catch (Exception ex) { Log.Debug("[Spiral] sfx {Tag} failed: {E}", tag, ex.Message); }
    }
}
