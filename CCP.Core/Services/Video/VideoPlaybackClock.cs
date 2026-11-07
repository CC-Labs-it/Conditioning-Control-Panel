using System;

namespace ConditioningControlPanel.Services.Video;

/// <summary>Counts advancing media time without charging skipped positions or paused time.</summary>
public sealed class VideoPlaybackClock
{
    private long _position, _sampleMs;
    private bool _observed;
    public long PlayedMs { get; private set; }
    public void ResetPosition() => _observed = false;

    public void Observe(long positionMs, long nowMs)
    {
        if (positionMs < 0) return;
        if (_observed && positionMs == _position) return;
        if (_observed && positionMs > _position)
            PlayedMs += Math.Min(positionMs - _position, Math.Max(0, nowMs - _sampleMs));
        _position = positionMs;
        _sampleMs = nowMs;
        _observed = true;
    }
}
