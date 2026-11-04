using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Services.Flash;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>One companion line per flash burst (WPF FlashService.cs:1535,4410,4663).</summary>
internal static class FlashVoice
{
    /// <summary>WPF PlaySound's length when the clip fails (FlashService.cs:4726).</summary>
    internal const double FailedSeconds = 5.0;
    /// <summary>WPF ShowImages: the duck is released 1.5 s after the clip (FlashService.cs:1556).</summary>
    internal const int UnduckAfterMs = 1500;
    /// <summary>A clip that neither starts nor fails by then counts as failed, so the burst still shows.</summary>
    internal const int StartTimeoutMs = 2000;

    private static Action? _stop;
    // WPF GetNextSound's shuffled queue (:4410): every eligible line plays before any repeats.
    internal static readonly ShuffleBag<string> Bag = new(p => p, new Random());

    /// <summary>Deferred work (tests swap it). The unduck is never cancelled, as WPF ScheduleUnduck (#1107).</summary>
    internal static Action<int, Action> Later = (ms, a) => _ = Task.Delay(ms).ContinueWith(_ =>
    {
        try { a(); } catch (Exception ex) { Log.Debug(ex, "Flash voice deferred step failed"); }
    });

    /// <summary>The burst's entry point (tests swap it).</summary>
    internal static Action<AppSettings, Action<double?>> Speak = Play;

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

    /// <summary>Starts the next line. <paramref name="ready"/> runs once, on any thread, with the
    /// clip's length in seconds (<see cref="FailedSeconds"/> when it fails), or null when no line plays.</summary>
    internal static void Play(AppSettings s, Action<double?> ready)
    {
        var files = new List<string>();
        if (Allowed(s))
        {
            try
            {
                // The library merges content roots; include category subfolders as WPF does.
                files = CompanionVoiceLines.GetRows(s).Where(p => p.IsEnabled)
                    .Select(p => p.AudioFilePath).Where(p => p != null).Cast<string>().ToList();
                var folder = CompanionVoiceLines.ResolveFolder().Absolute;
                if (Directory.Exists(folder))
                    files.AddRange(Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                        .Where(p => new[] { ".mp3", ".wav", ".ogg" }.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase)));
                files = Filter(s, files);
            }
            catch (Exception ex) { files.Clear(); Log.Debug(ex, "Flash voice selection failed"); }
        }
        Play(s, files, ready);
    }

    internal static void Play(AppSettings s, IReadOnlyList<string> files, Action<double?> ready)
    {
        Stop();   // WPF PlaySound: StopCurrentSound first
        string? path = null;
        if (Allowed(s)) lock (Bag) path = Bag.TryNext(files, out var p) ? p : null;
        if (path == null) { ready(null); return; }

        long? duckGen = null;
        if (s.AudioDuckingEnabled) { CoreAudio.Duck(s.DuckingLevel); duckGen = CoreAudio.DuckGeneration; }
        var settled = 0;
        void Settle(double seconds)
        {
            if (Interlocked.Exchange(ref settled, 1) != 0) return;
            if (duckGen is { } g) Later((int)(seconds * 1000) + UnduckAfterMs, () => CoreAudio.Unduck(g));
            ready(seconds);
        }

        var master = s.MasterVolume / 100f;
        var gain = CompanionVoiceGain.ApplyLevel(Math.Max(0.05f, (float)Math.Pow(master, 1.5))
            * CompanionVoiceGain.BarkTrim, s.CompanionVoiceLevel);
        Later(StartTimeoutMs, () => Settle(FailedSeconds));
        _stop = CoreAudio.PlayStoppable(path, gain, "flash-voice",
            onStarted: len => Settle(len > TimeSpan.Zero ? len.TotalSeconds : FailedSeconds),
            onFinished: () => Settle(FailedSeconds));
    }
}
