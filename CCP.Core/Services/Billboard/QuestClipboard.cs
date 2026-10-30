using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Billboard;

/// <summary>Quest clipboard counts and stamp timing, shared without drawing types.</summary>
public static class QuestClipboard
{
    public const int MaxSlips = 5;
    public const double LoopStart = 1.4, LoopSeconds = 5.2;

    public static (int Done, int Total) Data(object? data)
    {
        int? Get(string key) => data is IReadOnlyDictionary<string, int> d && d.TryGetValue(key, out var v) ? v : null;
        int total = Math.Max(0, Get("total") ?? 3);
        return (Math.Clamp(Get("done") ?? 0, 0, total), total);
    }

    public static (int Shown, int Done) Fit(int done, int total)
    {
        total = Math.Max(1, total);
        done = Math.Clamp(done, 0, total);
        int shown = Math.Min(total, MaxSlips);
        return (shown, Math.Max(0, shown - (total - done)));
    }

    public static int StampTarget(double t, int doneShown) =>
        doneShown <= 0 || t < LoopStart ? -1 : (int)Math.Floor((t - LoopStart) / LoopSeconds) % doneShown;

    public static double BoardHeight(double unit, int n)
    {
        n = Math.Clamp(n, 1, MaxSlips);
        double sh = n <= 3 ? 0.15 : 0.125;
        return unit * Math.Clamp(0.155 + n * sh + (n - 1) * 0.025, 0.5, 0.86);
    }
}
