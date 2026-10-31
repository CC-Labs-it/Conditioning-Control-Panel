using System;

namespace ConditioningControlPanel.Services.Video;

/// <summary>WPF VideoService.cs:6568-6650: duration plus five seconds, or the unknown-length
/// fallback. Voice holds leave this deadline active. Only a grace pause suspends it (#735).</summary>
public sealed class VideoSafetyDeadline
{
    private DateTime? _deadline, _graceStarted;

    public void Arm(DateTime now, double seconds)
    {
        _deadline = now.AddSeconds(seconds > 0 ? seconds + 5 : MandatoryVideoScheduler.FallbackSeconds);
        if (_graceStarted != null) _graceStarted = now;
    }

    public void PauseForGrace(DateTime now) => _graceStarted ??= now;

    public void ResumeFromGrace(DateTime now)
    {
        if (_graceStarted is { } start && _deadline is { } end) _deadline = end + (now - start);
        _graceStarted = null;
    }

    public bool Expired(DateTime now) => _graceStarted == null && _deadline is { } end && now >= end;

    public void Reset() => (_deadline, _graceStarted) = (null, null);
}
