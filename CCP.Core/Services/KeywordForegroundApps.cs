using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services;

/// <summary>WPF KeywordTriggerService's eight distinct recent processes. Memory only.</summary>
public sealed class KeywordForegroundApps
{
    private readonly LinkedList<string> _apps = new();
    public IReadOnlyList<string> Recent => _apps.ToArray();

    public void Remember(string? process, bool own)
    {
        if (own || string.IsNullOrWhiteSpace(process)) return;
        var existing = _apps.FirstOrDefault(a => string.Equals(a, process, StringComparison.OrdinalIgnoreCase));
        if (existing != null) _apps.Remove(existing);
        _apps.AddFirst(existing ?? process);
        while (_apps.Count > 8) _apps.RemoveLast();
    }
}
