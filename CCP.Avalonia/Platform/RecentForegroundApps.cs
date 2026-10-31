using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Avalonia.Platform;

// WPF KeywordTriggerService.cs:489-520,582,618-622. Session-only process names.
internal sealed class RecentForegroundApps
{
    private readonly LinkedList<string> _apps = new();
    internal Func<(string? Process, bool Own)> Source = KeywordTriggerHead.ReadForegroundProcess;
    internal string[] Snapshot() => _apps.ToArray();
    internal void Sample()
    {
        try
        {
            var (name, own) = Source();
            if (own || string.IsNullOrWhiteSpace(name)) return;
            var existing = _apps.FirstOrDefault(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
            if (existing != null) _apps.Remove(existing);
            _apps.AddFirst(existing ?? name);
            while (_apps.Count > 8) _apps.RemoveLast();
        }
        catch { /* Foreground processes may exit during lookup. */ }
    }
    internal void Clear() => _apps.Clear();
}
