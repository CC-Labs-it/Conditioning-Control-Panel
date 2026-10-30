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
    /// <summary>WPF PaintStamp: descend, wind up, strike, squash, then lift.</summary>
    public static (double Y, double Alpha, double Tilt, double Sx, double Sy) StampFrame(double p, double sh, double bh)
    {
        double y, alpha = 1, tilt = 0, sx = 1, sy = 1, hover = -sh * 0.9;
        double Ease(double k) => 1 - Math.Pow(1 - Math.Clamp(k, 0, 1), 3);
        if (p < 1.2) { double e = Ease((p - 0.6) / 0.6); y = -bh * 0.75 + (hover + bh * 0.75) * e; alpha = e; tilt = (1 - e) * 10; }
        else if (p < 1.5) { double k = Math.Sin((p - 1.2) / 0.3 * Math.PI / 2); y = hover - k * sh * 0.35; tilt = -4 * k; sy = 1 + 0.06 * k; sx = 1 - 0.04 * k; }
        else if (p < 1.62) { double k = (p - 1.5) / 0.12; y = (hover - sh * 0.35) * (1 - k * k); tilt = -4 * (1 - k); sy = 1.08; sx = 0.95; }
        else if (p < 2.3) { y = 0; sy = 1 - 0.22 * Math.Exp(-(p - 1.62) * 9) * Math.Cos((p - 1.62) * 28); sx = 2 - sy; }
        else { double e = Ease((p - 2.3) / 0.9); y = -e * bh * 0.75; alpha = 1 - e; tilt = e * 8; }
        return (y, alpha, tilt, sx, sy);
    }
}
