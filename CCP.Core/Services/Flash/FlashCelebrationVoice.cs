using System;
using System.IO;
using System.Threading;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Flash;

/// <summary>WPF FlashService.PlayRandomSound / PlaySound: a celebration ignores the ambient
/// FlashAudioEnabled switch, cuts the previous clip, and uses the master volume curve.
/// WPF Services/Flash/FlashService.cs:4485, :4682.</summary>
internal static class FlashCelebrationVoice
{
    private static Action? _stop;

    internal static void Play(AppSettings settings)
    {
        // WPF PlayRandomSound does not apply the ambient avatar/voice-line mute gates.
        // Master mute still produces zero volume (the curve's floor never unmutes it).
        try
        {
            var path = FlashVoicePool.Next(settings);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            Stop();
            _stop = CoreAudio.PlayStoppable(path, CompanionVoiceGain.ApplyLevel(FlashVoicePool.Volume(settings.MasterVolume), settings.CompanionVoiceLevel), "flash-sound");
        }
        catch (Exception ex) { Serilog.Log.Debug("Quest voice failed: {E}", ex.Message); }
    }

    internal static void Stop() => Interlocked.Exchange(ref _stop, null)?.Invoke();
}
