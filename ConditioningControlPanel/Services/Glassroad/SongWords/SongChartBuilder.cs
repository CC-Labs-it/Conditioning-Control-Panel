using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Glassroad.SongWords;

/// <summary>One heard word: text, start and end in seconds, mean token probability, whisper segment index.</summary>
public sealed record SongWord(string Text, double StartS, double EndS, double P, int Line);

/// <summary>
/// Turns whisper words into a Glassroad chart for a player's own song (pure, no I/O). The page takes the same
/// shape as an authored chart (`audio/chart.js normalizeChart`): bpm, sections, lines, words with bubble marks.
/// Beats are not known here: `bpm` is a placeholder the page replaces from its own beat analysis when the chart
/// carries `userSong: true`. Word timing comes from whisper.cpp's token heuristics (spike 2026-10-31: about
/// half the words within 0.5 s, nine in ten within 1 s), so bubbles go only to confident longer words and the rest
/// is road paint; a wrong word on the floor costs nothing, a bubble at the wrong moment does.
/// </summary>
public static class SongChartBuilder
{
    public const int Version = 1;
    public const double DropBelowP = 0.30;
    public const double BubbleP = 0.60;
    public const int BubbleLetters = 3;
    public const int MaxLabel = 40;
    public const int PlaceholderBpm = 96;

    /// <summary>A stable id for one file: path, size and write time hashed (sha1, 16 hex chars). Matches the page's
    /// saved songId shape `^[a-z0-9-]{1,40}$`.</summary>
    public static string SongId(string path, long bytes, DateTime mtimeUtc)
    {
        var key = path.ToLowerInvariant() + "|" + bytes.ToString(CultureInfo.InvariantCulture) + "|" + mtimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
        var h = SHA1.HashData(Encoding.UTF8.GetBytes(key));
        return "u" + Convert.ToHexString(h, 0, 8).ToLowerInvariant();
    }

    /// <summary>The song row label: the file name without extension, trimmed to <see cref="MaxLabel"/> chars.</summary>
    public static string Label(string fileName)
    {
        var s = Path.GetFileNameWithoutExtension(fileName ?? "").Replace('_', ' ').Trim();
        foreach (var c in s) if (char.IsControl(c)) { s = new string(s.Where(ch => !char.IsControl(ch)).ToArray()); break; }
        if (s.Length == 0) s = "My song";
        return s.Length > MaxLabel ? s[..MaxLabel].TrimEnd() : s;
    }

    /// <summary>True for a word that is a transcript artefact ([MUSIC], (laughs), empty) rather than a lyric.</summary>
    public static bool IsNoise(string text)
    {
        var t = (text ?? "").Trim();
        if (t.Length == 0) return true;
        if (t[0] == '[' || t[0] == '(' || t[0] == '{' || t[^1] == ']' || t[^1] == ')') return true;
        return !t.Any(char.IsLetterOrDigit);
    }

    public static int Letters(string text) => (text ?? "").Count(char.IsLetter);

    /// <summary>Build the chart. Words are cleaned, clamped to the song, kept monotone, and marked for bubbles.</summary>
    public static JObject Build(string id, string label, double durationS, string audioFile, IReadOnlyList<SongWord> words, string model)
    {
        var dur = Math.Max(1, durationS);
        var outWords = new JArray();
        var lines = new JArray();
        var lineIndex = new Dictionary<int, int>();
        var lineText = new List<StringBuilder>();
        double last = 0;
        foreach (var w in words)   // transcript order; a token timed before its neighbour is pushed forward, never reordered
        {
            if (w.P < DropBelowP || IsNoise(w.Text)) continue;
            var text = w.Text.Trim();
            var start = Math.Clamp(w.StartS, 0, Math.Max(0, dur - 0.05));
            if (start < last) start = last;
            var end = Math.Clamp(Math.Max(w.EndS, start + 0.05), start, dur);
            last = start;
            if (!lineIndex.TryGetValue(w.Line, out var li)) { li = lineText.Count; lineIndex[w.Line] = li; lineText.Add(new StringBuilder()); }
            if (lineText[li].Length > 0) lineText[li].Append(' ');
            lineText[li].Append(text);
            var bubble = w.P >= BubbleP && Letters(text) >= BubbleLetters;
            outWords.Add(new JObject
            {
                ["w"] = text,
                ["startS"] = Math.Round(start, 3),
                ["endS"] = Math.Round(end, 3),
                ["line"] = li,
                ["bubble"] = bubble,
                ["gate"] = false,
                ["p"] = Math.Round(w.P, 2),
            });
        }
        foreach (var sb in lineText) lines.Add(sb.ToString());
        return new JObject
        {
            ["id"] = id,
            ["title"] = label,
            ["audio"] = audioFile,
            ["version"] = Version,
            ["userSong"] = true,
            ["model"] = model,
            ["durationS"] = Math.Round(dur, 2),
            ["bpm"] = PlaceholderBpm,
            ["sections"] = new JArray(new JObject { ["id"] = "s1", ["startS"] = 0, ["endS"] = Math.Round(dur, 2), ["mood"] = "groove", ["energy"] = 0.5 }),
            ["lines"] = lines,
            ["words"] = outWords,
        };
    }
}
