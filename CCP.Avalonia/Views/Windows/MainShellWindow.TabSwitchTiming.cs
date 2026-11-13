using System;
using System.Collections.Generic;
using System.Diagnostics;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    // Per-window observer for the headless benchmark; disabled tracing allocates no timing state.
    internal Action<string, IReadOnlyDictionary<string, double>>? TabSwitchMeasured;
}

/// <summary>Opt-in synchronous ShowTab stages. CCP_TRACE_TAB_SWITCH=1 logs milliseconds at Debug.
/// Layout/render happen after ShowTab returns and are measured separately by PerfBench.</summary>
internal sealed class TabSwitchTiming : IDisposable
{
    internal static readonly bool Enabled = Environment.GetEnvironmentVariable("CCP_TRACE_TAB_SWITCH") == "1";
    private readonly string _tab;
    private readonly Action<string, IReadOnlyDictionary<string, double>>? _report;
    private readonly Dictionary<string, double> _stages = new();
    private readonly long _start = Stopwatch.GetTimestamp();
    private long _last;

    private TabSwitchTiming(string tab, Action<string, IReadOnlyDictionary<string, double>>? report)
    {
        _tab = tab;
        _report = report;
        _last = _start;
    }

    internal static TabSwitchTiming? Start(string tab, Action<string, IReadOnlyDictionary<string, double>>? report) =>
        Enabled || report != null ? new(tab, report) : null;

    internal void Mark(string stage)
    {
        var now = Stopwatch.GetTimestamp();
        _stages[stage] = Stopwatch.GetElapsedTime(_last, now).TotalMilliseconds;
        _last = now;
    }

    public void Dispose()
    {
        _stages["show-tab"] = Stopwatch.GetElapsedTime(_start).TotalMilliseconds;
        if (Enabled) Log.Debug("[TabSwitch] {Tab} {@StagesMs}", _tab, _stages);
        _report?.Invoke(_tab, _stages);
    }
}
