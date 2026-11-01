using System;
using System.Collections.Generic;
using Serilog;

namespace ConditioningControlPanel;

/// <summary>Engine-off audio diagnostics. File names only, one line per player per ten seconds.</summary>
public static class CoreAudioDiagnostics
{
    public static readonly TimeSpan MinGap = TimeSpan.FromSeconds(10);
    private static readonly Dictionary<string, DateTime> Last = new(StringComparer.Ordinal);
    private static readonly object Gate = new();
    public static bool EngineRunning => IsRunning(CoreSession.IsEngineRunning, CoreSession.IsSessionRunning);
    public static bool IsRunning(bool engineRunning, bool sessionRunning) => engineRunning || sessionRunning;

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
            var line = HeldLineFor(player, "audio", DateTime.UtcNow);
            if (line != null) Log.Information("[VideoDiag] AUDIO {AudioEvent}", line);
        }
        catch { /* A diagnostic must not interrupt the trigger. */ }
    }

    public static string? LineFor(string player, string? path, bool engineRunning, bool asked, DateTime nowUtc)
    {
        if (engineRunning || asked) return null;
        if (string.IsNullOrEmpty(player)) player = "audio";
        lock (Gate)
        {
            if (Last.TryGetValue(player, out var last) && nowUtc >= last && nowUtc - last < MinGap)
                return null;
            Last[player] = nowUtc;
        }
        return $"engine off: {player} started '{NameOf(path)}'";
    }

    public static string? HeldLineFor(string player, string what, DateTime nowUtc) =>
        LineFor(player + ":held", null, false, false, nowUtc) == null
            ? null : $"engine off: {player} held its {what}";

    internal static void ResetForTests()
    {
        lock (Gate) Last.Clear();
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
