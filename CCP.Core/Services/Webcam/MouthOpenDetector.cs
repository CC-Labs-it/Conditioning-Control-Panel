using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services;

/// <summary>Frame-free WPF MAR detector. Capture-thread only; never retains landmarks or frames.</summary>
public sealed class MouthOpenDetector
{
    public const double OpenRatio = 1.65, CloseRatio = 1.30;
    private readonly Queue<double> _closed = new(), _smooth = new();
    private DateTime? _openedAt;
    private DateTime _lastFire = DateTime.MinValue;
    public bool IsOpen { get; private set; }
    public double Baseline { get; private set; }
    public int ClosedSamples => _closed.Count;
    public int Count { get; private set; }
    public double OpeningMax { get; private set; }
    public double WindowMin { get; private set; } = double.MaxValue;
    public double WindowMax { get; private set; } = double.MinValue;

    public static double ComputeMar(float[][] landmarks)
    {
        static double Distance(float[] a, float[] b)
        {
            double dx = a[0] - b[0], dy = a[1] - b[1];
            return Math.Sqrt(dx * dx + dy * dy);
        }
        double width = Distance(landmarks[78], landmarks[308]);
        return width > 1e-6 ? (Distance(landmarks[81], landmarks[178]) + Distance(landmarks[13], landmarks[14])
            + Distance(landmarks[311], landmarks[402])) / (3 * width) : 0;
    }

    public bool Update(double rawMar, DateTime now, double sensitivity = 0.5)
    {
        static void Feed(Queue<double> q, double value, int cap) { q.Enqueue(value); while (q.Count > cap) q.Dequeue(); }
        Feed(_smooth, rawMar, 3);
        double mar = BlinkDetector.PercentileOf(_smooth, .50);
        if (!IsOpen || _closed.Count < 15) Feed(_closed, mar, 90);
        Baseline = BlinkDetector.PercentileOf(_closed, .10);
        WindowMin = Math.Min(WindowMin, mar);
        WindowMax = Math.Max(WindowMax, mar);
        if (_closed.Count < 15 || Baseline <= 0) return false;
        double scale = 1 + (.5 - Math.Clamp(sensitivity, 0, 1)) * .8;
        double absoluteOpen = .38 * scale;
        bool open = IsOpen
            ? mar > CloseRatio * Baseline || (.28 > Baseline && mar > .28)
            : (mar > OpenRatio * scale * Baseline && mar > .32 * scale) || (absoluteOpen > Baseline && mar > absoluteOpen);
        bool fired = false;
        if (open && !IsOpen) { _openedAt = now; OpeningMax = mar; }
        else if (open)
        {
            OpeningMax = Math.Max(OpeningMax, mar);
            if (_openedAt.HasValue && _lastFire < _openedAt.Value
                && (now - _openedAt.Value).TotalMilliseconds >= 250 && (now - _lastFire).TotalMilliseconds >= 800)
            { _lastFire = now; Count++; fired = true; }
        }
        else if (IsOpen) { _openedAt = null; OpeningMax = 0; }
        IsOpen = open;
        return fired;
    }

    public void FaceMissing() { IsOpen = false; _openedAt = null; _smooth.Clear(); }
    public void ResetDiagnosticWindow() { WindowMin = double.MaxValue; WindowMax = double.MinValue; }
    /// <summary>Matches WPF restart: drop baselines but retain the last-fire cooldown.</summary>
    public void Reset()
    {
        FaceMissing(); _closed.Clear(); Baseline = 0; OpeningMax = 0; Count = 0; ResetDiagnosticWindow();
    }
}
