using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Awareness;

/// <summary>The WPF keyword service's process-name list and foreground-scope rules.
/// The heads resolve windows; these rules never inspect the desktop.</summary>
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

    /// <summary>Null means allowed. Unknown foreground windows fail closed only for an allow list;
    /// own-focus suppression takes precedence over a known process's list membership.</summary>
    public static string? SuppressionReason(AwarenessAppScope scope, IEnumerable<string>? apps,
        string processName, bool known, bool isOwn, bool ignoreOwnFocus)
    {
        if (!known)
            return scope == AwarenessAppScope.OnlyListed ? "foreground window could not be identified" : null;
        if (isOwn && ignoreOwnFocus) return "Control Panel itself has focus";
        if (scope == AwarenessAppScope.ExceptListed && MatchesAppList(apps, processName))
            return $"'{processName}' is on the block list";
        if (scope == AwarenessAppScope.OnlyListed && !MatchesAppList(apps, processName))
            return $"'{processName}' is not on the allow list";
        return null;
    }
}
