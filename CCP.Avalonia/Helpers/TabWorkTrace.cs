using System;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.IO;

namespace ConditioningControlPanel.Avalonia.Helpers;

// Opt-in CPU stage timings; the desktop bench still owns rendered-frame latency.
internal sealed class TabWorkTrace : IDisposable
{
    private static readonly ConcurrentDictionary<string, byte> FailedFiles = new(StringComparer.Ordinal);
    private static TextWriter? _failedConsole;
    private readonly string _tab;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _last;
    private TabWorkTrace(string tab) => _tab = tab;
    internal static TabWorkTrace? Start(string tab) =>
        Environment.GetEnvironmentVariable("CCP_TRACE_TAB_SWITCH") == "1" ? new(tab) : null;
    internal void Mark(string stage)
    {
        var now = _clock.Elapsed.TotalMilliseconds;
        var line = $"tab-stage {_tab} {stage} {now - _last:F3}";
        // Diagnostics must never become a navigation/startup dependency, even in Dispose.
        var console = Console.Out;
        if (!ReferenceEquals(console, _failedConsole))
        {
            try { console.WriteLine(line); }
            catch { _failedConsole = console; }
        }
        if (Environment.GetEnvironmentVariable("CCP_TRACE_TAB_SWITCH_FILE") is { Length: > 0 } path &&
            !FailedFiles.ContainsKey(path))
        {
            try { File.AppendAllText(path, line + Environment.NewLine); }
            catch { FailedFiles.TryAdd(path, 0); }
        }
        _last = now;
    }
    public void Dispose() => Mark("end");
}
