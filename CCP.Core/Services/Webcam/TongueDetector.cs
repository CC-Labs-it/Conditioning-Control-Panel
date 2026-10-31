using System;

namespace ConditioningControlPanel.Services.Webcam;

/// <summary>WPF WebcamTrackingService's HSV tongue timing, without frames or UI. Mouth state comes from
/// Core <see cref="MouthOpenDetector"/>; the head supplies the fraction of useful lip pixels that are pink.
/// Capture-thread only.</summary>
public sealed class TongueDetector
{
    private DateTime? _since;
    private DateTime _last = DateTime.MinValue;
    private bool _out;

    /// <summary>Cancels a pending hold; keeps the cooldown, as WPF does.</summary>
    public void FaceMissing()
    {
        _out = false;
        _since = null;
    }

    public bool Update(bool mouthOpen, double ratio, DateTime now)
    {
        if (!mouthOpen) { FaceMissing(); return false; }
        bool visible = ratio > (_out ? 0.18 : 0.28);
        bool fire = false;
        if (visible && !_out) _since = now;
        else if (visible && _since is { } start && _last < start
                 && (now - start).TotalMilliseconds >= 350
                 && (now - _last).TotalMilliseconds >= 700)
        {
            _last = now;
            fire = true;
        }
        else if (!visible && _out) _since = null;
        _out = visible;
        return fire;
    }
}
