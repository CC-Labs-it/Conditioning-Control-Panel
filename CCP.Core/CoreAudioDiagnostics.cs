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
    private static readonly Queue<string> TailLines = new();
    public static bool EngineRunning => IsRunning(CoreSession.IsEngineRunning, CoreSession.IsSessionRunning);
    public static bool IsRunning(bool engineRunning, bool sessionRunning) => engineRunning || sessionRunning;
    public static void Note(string player, string? path, bool asked = false)
    {
        try
        {
            var line = LineFor(player, path, EngineRunning, asked, DateTime.UtcNow);
            if (line != null) Record(line);
        }
        catch { /* Diagnostics must never cost a sound. */ }
    }
    public static void NoteHeld(string player, string what = "audio")
    {
        try
        {
            var line = HeldLineFor(player, what, DateTime.UtcNow);
            if (line != null) Record(line);
        }
        catch { /* A diagnostic must not interrupt the trigger. */ }
    }
    private static void Record(string line)
    {
        var entry = $"{DateTime.UtcNow:O} [VideoDiag] AUDIO {line}";
        lock (Gate)
        {
            TailLines.Enqueue(entry.Length > 2048 ? entry[..2048] : entry);
            while (TailLines.Count > 200) TailLines.Dequeue();
        }
        Log.Information("[VideoDiag] AUDIO {AudioEvent}", line);
    }
    public static string Tail(int maxLines)
    {
        lock (Gate) return string.Join(Environment.NewLine, System.Linq.Enumerable.TakeLast(TailLines, Math.Max(0, maxLines)));
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
        lock (Gate) { Last.Clear(); TailLines.Clear(); }
    }
    // Both separators: bug reports must not expose Windows user paths on Linux either.
    public static string NameOf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "?";
        var trimmed = path.TrimEnd('/', '\\');
        var at = Math.Max(trimmed.LastIndexOf('/'), trimmed.LastIndexOf('\\'));
        if (at < 0 && trimmed.Length >= 2 && char.IsAsciiLetter(trimmed[0]) && trimmed[1] == ':') at = 1;
        return at >= trimmed.Length - 1 ? "?" : trimmed[(at + 1)..];
    }
}
