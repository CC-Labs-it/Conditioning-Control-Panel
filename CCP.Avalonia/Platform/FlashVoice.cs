using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>One companion line per flash burst (WPF FlashService.cs:1535,4410,4663).</summary>
internal static class FlashVoice
{
    private static Action? _stop;

    internal static void Stop()
    {
        var stop = _stop;
        _stop = null;
        try { stop?.Invoke(); }
        catch (Exception ex) { Log.Debug(ex, "Flash voice stop failed"); }
    }

    internal static bool Allowed(AppSettings s) => s.FlashAudioEnabled && s.MasterVolume > 0
        && !s.AvatarMuted && !s.CompanionVoiceLinesMuted;

    internal static List<string> Filter(AppSettings s, IEnumerable<string> files) => files
        .Where(f => !s.DisabledPhraseIds.Contains(CompanionPhraseIds.VoiceLineId(f))
                 && !s.RemovedPhraseIds.Contains(CompanionPhraseIds.VoiceLineId(f)))
        .DistinctBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToList();

    internal static void Play(AppSettings s)
    {
        Stop();
        if (!Allowed(s)) return;
        try
        {
            // The library merges content roots; include category subfolders as WPF does.
            var files = CompanionVoiceLines.GetRows(s).Where(p => p.IsEnabled)
                .Select(p => p.AudioFilePath).Where(p => p != null).Cast<string>().ToList();
            var folder = CompanionVoiceLines.ResolveFolder().Absolute;
            if (Directory.Exists(folder))
                files.AddRange(Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                    .Where(p => new[] { ".mp3", ".wav", ".ogg" }.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase)));
            Play(s, Filter(s, files));
        }
        catch (Exception ex) { Log.Debug(ex, "Flash voice selection failed"); }
    }

    internal static void Play(AppSettings s, IReadOnlyList<string> files)
    {
        if (!Allowed(s) || files.Count == 0) return;
        var master = s.MasterVolume / 100f;
        var gain = CompanionVoiceGain.ApplyLevel(Math.Max(0.05f, (float)Math.Pow(master, 1.5))
            * CompanionVoiceGain.BarkTrim, s.CompanionVoiceLevel);
        _stop = CoreAudio.PlayStoppable(files[Random.Shared.Next(files.Count)], gain, "flash-voice");
    }
}
