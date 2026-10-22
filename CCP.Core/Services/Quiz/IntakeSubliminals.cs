using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Serilog;

namespace ConditioningControlPanel.Services.Quiz;

/// <summary>WPF IntakeHostService's bounded phrase/whisper payload, shared by both heads.</summary>
public static class IntakeSubliminals
{
    /// <summary>WPF's forty-phrase media sample; raw text is retained for the in-page effect layer.</summary>
    public static string[]? Sample(IDictionary<string, bool>? pool)
    {
        try
        {
            var active = pool?.Where(kvp => kvp.Value).Select(kvp => kvp.Key)
                .Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
            if (active == null || active.Count == 0) return null;
            var rng = new Random();
            for (int i = active.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (active[i], active[j]) = (active[j], active[i]);
            }
            return active.Take(40).ToArray();
        }
        catch (Exception ex)
        {
            Log.Debug("IntakeSubliminals.Sample: {E}", ex.Message);
            return null;
        }
    }

    // Audio inlined into the init config: only enabled phrases, and only when a matching clip
    // exists. Small per-clip + total budgets keep the message light (the whisper mp3s are
    // ~15-55KB each); the page re-guards clip length (>8s skipped) at play time.
    private const long SubClipMaxBytes = 512 * 1024;          // skip a single clip larger than this
    private const long SubAudioBudgetBytes = 6 * 1024 * 1024; // total raw audio inlined per launch
    private static readonly string[] SubAudioExtsLower = { ".mp3", ".wav", ".ogg" };

    /// <summary>The user's ENABLED subliminal phrases as <c>{ text, audio? }</c> entries for the
    /// web core's render/subliminals.js. <c>audio</c> is a base64 data: URI of the phrase's
    /// connected whisper clip when one exists (resolved with the SAME text→file matching
    /// <c>SubliminalService</c> uses: active-mod
    /// <c>resources/sounds/flashes_audio</c> first, then the default <c>Resources/sub_audio</c>).
    /// Shuffled + capped so the init message stays light; null when no phrases are enabled.</summary>
    public static object[]? Build(IDictionary<string, bool>? pool, bool audioEnabled, string? modAudioDir, string? defaultAudioDir)
    {
        try
        {
            var active = pool?.Where(kvp => kvp.Value).Select(kvp => kvp.Key)
                .Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToList();
            if (active == null || active.Count == 0) return null;

            var rng = new Random();
            for (int i = active.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (active[i], active[j]) = (active[j], active[i]);
            }
            if (active.Count > 400) active = active.GetRange(0, 400);

            long budget = SubAudioBudgetBytes;
            var list = new List<object>(active.Count);
            foreach (var text in active)
            {
                string? dataUri = null;
                if (audioEnabled)
                {
                    try
                    {
                        var file = ResolveSubliminalAudioFile(text, modAudioDir, defaultAudioDir);
                        if (file != null)
                        {
                            var len = new FileInfo(file).Length;
                            if (len > 0 && len <= SubClipMaxBytes && len <= budget)
                            {
                                var bytes = File.ReadAllBytes(file);
                                dataUri = "data:" + MimeForAudio(file) + ";base64," + Convert.ToBase64String(bytes);
                                budget -= bytes.Length;
                            }
                        }
                    }
                    catch { dataUri = null; } // any read failure = text-only for this phrase
                }

                list.Add(dataUri != null ? (object)new { text, audio = dataUri } : new { text });
            }
            return list.ToArray();
        }
        catch (Exception ex)
        {
            Log.Debug("IntakeHostService.BuildSubliminalPool: {E}", ex.Message);
            return null;
        }
    }

    /// <summary>Find the whisper clip for <paramref name="text"/>, mirroring
    /// <c>SubliminalService.FindLinkedAudio</c>: exact filename match against case/apostrophe
    /// variants, then a case-insensitive directory scan. Mod dir wins over the default dir.</summary>
    private static string? ResolveSubliminalAudioFile(string text, string? modDir, string? defaultDir)
    {
        var clean = text.Trim();
        // Stricter than WPF (which had no guard): a phrase holding a path separator or NUL never
        // becomes a file name, so a phrase cannot reach a clip outside the two audio folders.
        if (clean.Contains('/') || clean.Contains('\\') || clean.Contains('\0')) return null;
        var variants = new[]
        {
            clean,
            clean.ToUpperInvariant(),
            clean.ToLowerInvariant(),
            clean.Replace('’', '\''),
            clean.Replace('\'', '’'),
            clean.ToUpperInvariant().Replace('’', '\''),
        };
        var exts = new[] { ".mp3", ".wav", ".ogg", ".MP3", ".WAV", ".OGG" };

        foreach (var dir in new[] { modDir, defaultDir })
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;

            foreach (var v in variants)
                foreach (var ext in exts)
                {
                    var p = Path.Combine(dir, v + ext);
                    if (File.Exists(p)) return p;
                }

            try
            {
                var norm = clean.ToUpperInvariant().Replace('’', '\'');
                foreach (var f in Directory.GetFiles(dir))
                {
                    if (Array.IndexOf(SubAudioExtsLower, Path.GetExtension(f).ToLowerInvariant()) < 0) continue;
                    var name = Path.GetFileNameWithoutExtension(f).ToUpperInvariant().Replace('’', '\'');
                    if (name == norm) return f;
                }
            }
            catch { /* scan is best-effort */ }
        }
        return null;
    }

    private static string MimeForAudio(string file) => Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".wav" => "audio/wav",
        ".ogg" => "audio/ogg",
        _ => "audio/mpeg",
    };

}
