using System;

namespace ConditioningControlPanel.Services.Audio;

// WPF's public rules facade. Linked into Core tests so both callers exercise the same budget.
public static partial class EngineOffAudioLog
{
    public static readonly TimeSpan MinGap = CoreAudioDiagnostics.MinGap;

    public static bool IsRunning(bool engineRunning, bool sessionRunning) =>
        CoreAudioDiagnostics.IsRunning(engineRunning, sessionRunning);

    public static string? LineFor(string player, string? path, bool engineRunning, bool asked, DateTime nowUtc) =>
        CoreAudioDiagnostics.LineFor(player, path, engineRunning, asked, nowUtc);

    public static string NameOf(string? path) => CoreAudioDiagnostics.NameOf(path);

    internal static void ResetForTests() => CoreAudioDiagnostics.ResetForTests();
}
