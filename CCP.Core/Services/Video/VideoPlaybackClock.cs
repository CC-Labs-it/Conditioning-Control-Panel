using System;

namespace ConditioningControlPanel.Services.Video;

/// <summary>Counts advancing media time without charging skipped positions or paused time.</summary>
public sealed class VideoPlaybackClock
{
    private long _position, _sampleMs;
    private bool _observed;
    public long PlayedMs { get; private set; }
    public void ResetPosition() => _observed = false;

    public void Observe(long positionMs, long nowMs, long repeatLengthMs = 0)
    {
        if (positionMs < 0) return;
        if (_observed && positionMs == _position) return;
        if (_observed && positionMs > _position)
            PlayedMs += Math.Min(positionMs - _position, Math.Max(0, nowMs - _sampleMs));
        else if (_observed && repeatLengthMs > 0 && positionMs < _position)
        {
            var elapsed = Math.Max(0, nowMs - _sampleMs);
            var wrapped = repeatLengthMs - _position + positionMs;
            // Only count a plausible wrap, never a backward seek.
            if (wrapped >= 0 && wrapped <= elapsed + 250) PlayedMs += Math.Min(wrapped, elapsed);
        }
        _position = positionMs;
        _sampleMs = nowMs;
        _observed = true;
    }
}
