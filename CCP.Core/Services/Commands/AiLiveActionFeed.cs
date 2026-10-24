using System;
using System.Collections.ObjectModel;

namespace ConditioningControlPanel.Services.Commands;

/// <summary>The WPF live-actions feed's timestamp and last-30 policy. Heads own UI dispatch.</summary>
public static class AiLiveActionFeed
{
    public static void Append(ObservableCollection<string> feed, string line, DateTime localTime)
    {
        feed.Add($"[{localTime:HH:mm:ss}] {line}");
        while (feed.Count > AiCommandService.MaxLiveActions) feed.RemoveAt(0);
    }

    public static (string Timestamp, string Action) Parts(string line)
    {
        var separator = line.IndexOf("] ", StringComparison.Ordinal);
        return separator >= 0 ? (line[..(separator + 1)], line[(separator + 2)..]) : ("", line);
    }
}
