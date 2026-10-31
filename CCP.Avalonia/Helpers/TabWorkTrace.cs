using System;
using System.Diagnostics;

namespace ConditioningControlPanel.Avalonia.Helpers;

// Opt-in CPU stage timings; the desktop bench still owns rendered-frame latency.
internal sealed class TabWorkTrace : IDisposable
{
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
        Console.WriteLine(line);
        if (Environment.GetEnvironmentVariable("CCP_TRACE_TAB_SWITCH_FILE") is { Length: > 0 } path)
            System.IO.File.AppendAllText(path, line + Environment.NewLine);
        _last = now;
    }
    public void Dispose() => Mark("end");
}
