using System;
using System.IO;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Helpers;

/// <summary>Adapted from WPF Services/Descent/DescentRoomSfx.cs: the room's two entry notes.</summary>
internal static class DescentRoomSfx
{
    private static bool _missingLogged;

    internal static void PlayFogEntry() => Play("descent_fog_enter", "ui_deepen", 0.42f, "descent-fog-enter");
    internal static void PlaySplashOpen() => Play("descent_spiral_open", "reveal_chime", 0.34f, "descent-spiral-open");

    private static void Play(string dedicated, string fallback, float scale, string tag)
    {
        try
        {
            var s = CoreSettings.Current;
            if (!(App.DescentCountdown?.AudioEnabled ?? true) || s.MasterVolume <= 0) return;
            var path = Resolve(dedicated) ?? Resolve(fallback);
            if (path != null)
            {
                CoreAudio.PlayOneShot(path, Math.Clamp(s.MasterVolume / 100f * scale, 0f, 1f), tag);
                return;
            }
            if (_missingLogged) return;
            _missingLogged = true;
            Log.Debug("[Spiral] No sound for {Tag} - the room stays quiet.", tag);
        }
        catch (Exception ex) { Log.Debug("[Spiral] sfx {Tag} failed: {E}", tag, ex.Message); }
    }

    private static string? Resolve(string name)
    {
        var rel = $"chaos/{name}.mp3";
        var path = CoreModArt.AudioOverridePath(rel)
            ?? ContentLocator.Resolve(Path.Combine("Resources", "sounds", rel));
        return File.Exists(path) ? path : null;
    }
}
