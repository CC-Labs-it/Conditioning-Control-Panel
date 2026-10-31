using System;
using System.Collections.Generic;
using Serilog;

namespace ConditioningControlPanel;

/// <summary>Engine-off audio diagnostics. File names only, one line per player per ten seconds.</summary>
public static class CoreAudioDiagnostics
{
    private static readonly Dictionary<string, DateTime> Last = new(StringComparer.Ordinal);
    private static readonly object Gate = new();
    public static bool EngineRunning => CoreSession.IsEngineRunning || CoreSession.IsSessionRunning;

    public static void Note(string player, string? path, bool asked = false)
    {
        try
        {
            var line = LineFor(player, path, EngineRunning, asked, DateTime.UtcNow);
            if (line != null) Log.Information("[VideoDiag] AUDIO {AudioEvent}", line);
        }
        catch { /* Diagnostics must never cost a sound. */ }
    }

    public static void NoteHeld(string player)
    {
        try
        {
            if (LineFor(player + ":held", null, false, false, DateTime.UtcNow) != null)
                Log.Information("[VideoDiag] AUDIO engine off: {Player} held its audio", player);
        }
        catch { /* A diagnostic must not interrupt the trigger. */ }
    }

    public static string? LineFor(string player, string? path, bool engineRunning, bool asked, DateTime nowUtc)
    {
        if (engineRunning || asked) return null;
        if (string.IsNullOrEmpty(player)) player = "audio";
        lock (Gate)
        {
            if (Last.TryGetValue(player, out var last) && nowUtc >= last && nowUtc - last < TimeSpan.FromSeconds(10))
                return null;
            Last[player] = nowUtc;
        }
        return $"engine off: {player} started '{NameOf(path)}'";
    }

    // Both separators: bug reports must not expose Windows user paths on Linux either.
    public static string NameOf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "?";
        var trimmed = path.TrimEnd('/', '\\');
        var at = Math.Max(trimmed.LastIndexOf('/'), trimmed.LastIndexOf('\\'));
        return trimmed.Length == 0 ? "?" : trimmed[(at + 1)..];
    }
}
