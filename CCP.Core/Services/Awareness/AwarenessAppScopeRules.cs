using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Awareness;

/// <summary>The shared text rules for the Awareness app-scope editor and the trigger service.</summary>
public static class AwarenessAppScopeRules
{
    /// <summary>Whole process names, ignoring case, list-entry whitespace and an optional .exe suffix.</summary>
    public static bool MatchesAppList(IEnumerable<string>? list, string processName)
    {
        if (list == null || string.IsNullOrWhiteSpace(processName)) return false;
        foreach (var raw in list)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var entry = raw.Trim();
            if (entry.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                entry = entry.Substring(0, entry.Length - 4);
            if (entry.Length == 0) continue;
            if (string.Equals(entry, processName, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>Split comma/semicolon/newline entries, retaining the first spelling of each process.</summary>
    public static List<string> ParseAppList(string? text)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return result;
        foreach (var part in text.Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var entry = part.Trim();
            if (entry.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                entry = entry.Substring(0, entry.Length - 4);
            if (entry.Length == 0) continue;
            if (result.Any(e => string.Equals(e, entry, StringComparison.OrdinalIgnoreCase))) continue;
            result.Add(entry);
        }
        return result;
    }
}
