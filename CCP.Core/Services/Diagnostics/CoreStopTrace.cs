using System;

namespace ConditioningControlPanel.Services.Diagnostics;

/// <summary>One stop path's step timer (the WPF head's <c>StopTrace</c>, 7.4.2 freeze hunt).</summary>
public interface IStopTrace
{
    void Mark(string step);
    string? Finish();
}

/// <summary>Core's door to the head's stop timer: the timer writes the WPF video trace, so a head seeds
/// <see cref="BeginProvider"/> at startup. Unseeded (Avalonia, tests) a Core stop path is not timed.</summary>
public static class CoreStopTrace
{
    public static Func<string, IStopTrace>? BeginProvider { get; set; }

    public static IStopTrace Begin(string path) => BeginProvider?.Invoke(path) ?? Untimed.Instance;

    /// <summary>"Type.Method" of a handler (a lambda reads as its enclosing type plus the compiler name).</summary>
    public static string HandlerName(Delegate d)
    {
        var m = d.Method;
        var type = m.DeclaringType;
        while (type is { IsNested: true } && type.Name.StartsWith("<", StringComparison.Ordinal)) type = type.DeclaringType;
        return (type?.Name ?? "?") + "." + m.Name;
    }

    private sealed class Untimed : IStopTrace
    {
        public static readonly Untimed Instance = new();
        public void Mark(string step) { }
        public string? Finish() => null;
    }
}
