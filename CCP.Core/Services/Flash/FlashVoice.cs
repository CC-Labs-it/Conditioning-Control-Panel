using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Companion;

namespace ConditioningControlPanel.Services.Flash;

/// <summary>WPF FlashService BuildVoiceLinePool, PlaySound and ShowImages audio rules.</summary>
public sealed class FlashVoice
{
    private Action? _stop;
    private Action? _unduck;
    private int _generation;
    private readonly ShuffleBag<string> _bag = new(p => p, new Random());

    public static bool Enabled(AppSettings s) => s.FlashAudioEnabled && s.MasterVolume > 0
        && !s.AvatarMuted && !s.CompanionVoiceLinesMuted;

    // The floor applies to the curve, never to master mute. Voice level 0 retains clip timing.
    public static float Volume(AppSettings s) => s.MasterVolume <= 0 ? 0 : CompanionVoiceGain.ApplyLevel(
        Math.Max(0.05f, (float)Math.Pow(s.MasterVolume / 100f, 1.5)) * CompanionVoiceGain.BarkTrim,
        s.CompanionVoiceLevel);

    public static List<string> Pool(AppSettings s, IEnumerable<string> library, IEnumerable<string> extras)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return library.Concat(extras).Where(p => seen.Add(Path.GetFileName(p)))
            .Where(p => !s.RemovedPhraseIds.Contains(CompanionPhraseIds.VoiceLineId(p))
                && !s.DisabledPhraseIds.Contains(CompanionPhraseIds.VoiceLineId(p))).ToList();
    }

    public static TimeSpan Lifetime(AppSettings s, bool ambient, int? durationMs, TimeSpan? audioLength) =>
        FlashStayRule.Applies(s.FlashStayUntilPopped, s.FlashClickable, pointFired: !ambient)
            ? TimeSpan.FromMilliseconds(FlashStayRule.SafetyLifetimeMs)
            : (audioLength ?? TimeSpan.FromMilliseconds(durationMs ?? s.FlashDuration * 1000)) + TimeSpan.FromSeconds(1);

    public string? NextPath(AppSettings s)
    {
        var files = CompanionVoiceLines.GetFiles();
        if (CompanionVoiceLines.MigrateLegacyIds(s, files)) CoreSettings.Save();
        var folder = CompanionVoiceLines.ResolveFolder().Absolute;
        var extras = Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(p => Path.GetExtension(p).ToLowerInvariant() is ".mp3" or ".wav" or ".ogg") : Enumerable.Empty<string>();
        var pool = Pool(s, files, extras);
        foreach (var custom in s.CustomCompanionPhrases)
        {
            if (custom.Category != "VoiceLine" || !custom.Enabled || string.IsNullOrEmpty(custom.AudioFileName)) continue;
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds", "companion_audio", custom.AudioFileName);
            if (File.Exists(path)) pool.Add(path);
        }
        return _bag.TryNext(pool, out var picked) ? picked : null;
    }

    /// <summary>Clip duration arrives from the head's player. A failed start keeps authored timing.
    /// ponytail: the bounded wait prevents a broken audio device from holding a burst busy forever;
    /// a player that reports no length uses WPF's five-second failure fallback.</summary>
    public async Task<TimeSpan?> PlayAsync(string? path, AppSettings s)
    {
        Stop();
        if (!Enabled(s) || path == null || !File.Exists(path)) return null;
        var generation = _generation;
        var ready = new TaskCompletionSource<TimeSpan?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _stop = CoreAudio.PlayStoppable(path, Volume(s), "flash-voice",
            duration => ready.TrySetResult(duration > TimeSpan.Zero ? duration : TimeSpan.FromSeconds(5)),
            () => ready.TrySetResult(null));
        var winner = await Task.WhenAny(ready.Task, Task.Delay(2000));
        if (generation != _generation) return null;
        if (winner != ready.Task || await ready.Task is not { } length) { Stop(); return null; }
        if (s.AudioDuckingEnabled)
        {
            CoreAudio.Duck(s.DuckingLevel);
            var duckGeneration = CoreAudio.DuckGeneration;
            var released = 0;
            void Release() { if (Interlocked.Exchange(ref released, 1) == 0) CoreAudio.Unduck(duckGeneration); }
            _unduck = Release;
            // WPF #1107: never tie this restoration to the burst's cancellation token.
            _ = ReleaseLater(length + TimeSpan.FromMilliseconds(1500), Release);
        }
        return length;
    }

    private static async Task ReleaseLater(TimeSpan delay, Action release)
    {
        await Task.Delay(delay);
        release();
    }

    public void Stop()
    {
        _generation++;
        Interlocked.Exchange(ref _stop, null)?.Invoke();
        Interlocked.Exchange(ref _unduck, null)?.Invoke();
    }
}
