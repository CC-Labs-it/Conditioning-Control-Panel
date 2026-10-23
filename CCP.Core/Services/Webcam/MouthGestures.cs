using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Webcam;

/// <summary>WPF WebcamTrackingService's MAR/HSV gesture timing, without frames or UI.
/// Capture-thread only. The head supplies MAR and the fraction of useful lip pixels that are pink.</summary>
public sealed class MouthGestures
{
    private readonly Queue<double> _smooth = new(), _baseline = new();
    private DateTime? _openedAt, _tongueSince;
    private DateTime _lastOpen = DateTime.MinValue, _lastTongue = DateTime.MinValue;
    public bool IsOpen { get; private set; }
    private bool _tongueOut;

    public void Reset()
    {
        FaceMissing();
        _baseline.Clear();
    }

    // Keep the resting baseline and cooldowns through a face loss, as WPF does.
    public void FaceMissing()
    {
        IsOpen = _tongueOut = false;
        _openedAt = _tongueSince = null;
        _smooth.Clear();
    }

    public bool UpdateMouth(double rawMar, double sensitivity, DateTime now)
    {
        Push(_smooth, rawMar, 3);
        double mar = BlinkDetector.PercentileOf(_smooth, 0.50);
        if (!IsOpen || _baseline.Count < 15) Push(_baseline, mar, 90);
        double baseline = BlinkDetector.PercentileOf(_baseline, 0.10);
        if (_baseline.Count < 15 || baseline <= 0) return false;
        double scale = 1.0 + (0.5 - Math.Clamp(sensitivity, 0, 1)) * 0.8;
        double absoluteOpen = 0.38 * scale;
        bool open = IsOpen
            ? mar > 1.30 * baseline || (0.28 > baseline && mar > 0.28)
            : (mar > 1.65 * scale * baseline && mar > 0.32 * scale)
                || (absoluteOpen > baseline && mar > absoluteOpen);
        bool fire = false;
        if (open && !IsOpen) _openedAt = now;
        else if (open && _openedAt is { } start && _lastOpen < start
                 && (now - start).TotalMilliseconds >= 250
                 && (now - _lastOpen).TotalMilliseconds >= 800)
        {
            _lastOpen = now;
            fire = true;
        }
        else if (!open && IsOpen) _openedAt = null;
        IsOpen = open;
        return fire;
    }

    public bool UpdateTongue(double ratio, DateTime now)
    {
        if (!IsOpen) { _tongueOut = false; _tongueSince = null; return false; }
        bool visible = ratio > (_tongueOut ? 0.18 : 0.28);
        bool fire = false;
        if (visible && !_tongueOut) _tongueSince = now;
        else if (visible && _tongueSince is { } start && _lastTongue < start
                 && (now - start).TotalMilliseconds >= 350
                 && (now - _lastTongue).TotalMilliseconds >= 700)
        {
            _lastTongue = now;
            fire = true;
        }
        else if (!visible && _tongueOut) _tongueSince = null;
        _tongueOut = visible;
        return fire;
    }

    private static void Push(Queue<double> q, double value, int cap)
    {
        q.Enqueue(value);
        while (q.Count > cap) q.Dequeue();
    }
}
