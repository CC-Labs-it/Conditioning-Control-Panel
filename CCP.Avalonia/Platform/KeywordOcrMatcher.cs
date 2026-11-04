using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Platform;

// Screen coordinates are pixels. Screen identifies a monitor for regex rectangle unions.
internal sealed record KeywordOcrHit(string Text, Rectangle ScreenRect, int Screen = 0);

internal sealed class KeywordOcrMatcher
{
    private Dictionary<string, int> _seen = new();
    private readonly HashSet<string> _guarded = new();
    internal bool NeedsConfirmation { get; private set; }
    internal void Reset() { _seen.Clear(); _guarded.Clear(); NeedsConfirmation = false; }

    internal (List<KeywordTrigger> Triggers, List<KeywordOcrHit> Hits) Scan(
        IReadOnlyList<KeywordOcrHit> hits, AppSettings settings, KeywordCooldowns cooldowns, DateTime now)
    {
        NeedsConfirmation = false;
        if (hits == null || hits.Count == 0) { Reset(); return (new(), new()); }
        if ((now - cooldowns.LastGlobal).TotalSeconds < settings.KeywordGlobalCooldownSeconds) return (new(), new());
        var triggers = new List<KeywordTrigger>();
        var matches = new List<KeywordOcrHit>();
        foreach (var trigger in KeywordMatchRules.Ordered(settings.KeywordTriggers))
        {
            if (trigger == null || !trigger.Enabled || string.IsNullOrEmpty(trigger.Keyword) ||
                (now - trigger.LastTriggeredAt).TotalSeconds < trigger.CooldownSeconds ||
                cooldowns.IsMuted(trigger.Keyword, now.ToUniversalTime())) continue;
            var found = trigger.MatchType == KeywordMatchType.Regex
                ? RegexMatches(trigger.Keyword, hits) : WholeMatches(trigger.Keyword, hits);
            if (found.Count == 0) continue;
            triggers.Add(trigger); matches.AddRange(found);
        }
        if (matches.Count == 0) { Reset(); return (new(), new()); }
        var positions = new Dictionary<string, KeywordOcrHit>();
        foreach (var hit in matches)
        {
            var key = $"{hit.Text.ToLowerInvariant()}_{hit.ScreenRect.X / 120}_{hit.ScreenRect.Y / 120}";
            positions.TryAdd(key, hit);
        }
        _guarded.IntersectWith(positions.Keys);
        var seen = new Dictionary<string, int>();
        var candidates = new List<KeyValuePair<string, KeywordOcrHit>>();
        var required = Math.Max(1, settings.OcrConfirmationScans);
        foreach (var position in positions)
        {
            var count = (_seen.TryGetValue(position.Key, out var prior) ? prior : 0) + 1;
            seen[position.Key] = count;
            if (_guarded.Contains(position.Key)) continue;
            if (count < required) NeedsConfirmation = true;
            else candidates.Add(position);
        }
        _seen = seen;
        if (!settings.OcrHighlightAll && candidates.Count > 0)
        {
            for (var i = candidates.Count - 1; i > 0; i--)
            {
                var j = Random.Shared.Next(i + 1);
                (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
            }
            var count = Random.Shared.Next(1, candidates.Count + 1);
            candidates.RemoveRange(count, candidates.Count - count);
        }
        foreach (var pair in candidates) _guarded.Add(pair.Key);
        return candidates.Count == 0 ? (new(), new()) : (triggers, candidates.Select(p => p.Value).ToList());
    }

    internal static List<KeywordOcrHit> WholeMatches(string keyword, IReadOnlyList<KeywordOcrHit> hits)
    {
        var parts = keyword.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var result = new List<KeywordOcrHit>();
        if (parts.Length == 0) return result;
        for (var i = 0; i <= hits.Count - parts.Length; i++)
        {
            var matches = true;
            for (var j = 0; j < parts.Length; j++)
            {
                var text = hits[i + j].Text ?? "";
                var start = 0; var end = text.Length;
                while (start < end && !char.IsLetterOrDigit(text[start])) start++;
                while (end > start && !char.IsLetterOrDigit(text[end - 1])) end--;
                if (!text.Substring(start, end - start).Equals(parts[j], StringComparison.OrdinalIgnoreCase)) { matches = false; break; }
            }
            if (!matches) continue;
            if (parts.Length == 1) result.Add(hits[i]);
            else
            {
                var rect = hits[i].ScreenRect;
                for (var j = 1; j < parts.Length; j++) rect = Rectangle.Union(rect, hits[i + j].ScreenRect);
                result.Add(new(keyword, rect, hits[i].Screen));
            }
        }
        return result;
    }

    internal static List<KeywordOcrHit> RegexMatches(string pattern, IReadOnlyList<KeywordOcrHit> hits)
    {
        var result = new List<KeywordOcrHit>();
        var starts = new int[hits.Count];
        var text = new System.Text.StringBuilder();
        for (var i = 0; i < hits.Count; i++) { if (i > 0) text.Append(' '); starts[i] = text.Length; text.Append(hits[i].Text ?? ""); }
        try
        {
            foreach (Match match in Regex.Matches(text.ToString(), pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)))
            {
                if (match.Length == 0) continue;
                KeywordOcrHit? first = null; var rect = Rectangle.Empty;
                for (var i = 0; i < hits.Count; i++)
                {
                    if (starts[i] + (hits[i].Text?.Length ?? 0) <= match.Index || starts[i] >= match.Index + match.Length) continue;
                    if (first == null) { first = hits[i]; rect = first.ScreenRect; }
                    else if (hits[i].Screen == first.Screen) rect = Rectangle.Union(rect, hits[i].ScreenRect);
                }
                if (first != null) result.Add(new(match.Value, rect, first.Screen));
            }
        }
        catch (ArgumentException) { return new(); }
        catch (RegexMatchTimeoutException) { return new(); }
        return result;
    }
}
