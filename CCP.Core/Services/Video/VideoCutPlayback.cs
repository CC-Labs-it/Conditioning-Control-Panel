using System;

namespace ConditioningControlPanel.Services.Video;

/// <summary>A capped slice counts advancing media time, excluding its initial seek and pauses.</summary>
public sealed class VideoCutPlayback
{
    private long _position, _sampleMs;
    private bool _observed;
    public long PlayedMs { get; private set; }
    public void ResetPosition() => _observed = false;

    public static long StartOffsetMs(int maxSeconds, long durationMs, Random random)
        => maxSeconds <= 0 || durationMs <= maxSeconds * 1000L ? 0
            : random.NextInt64(durationMs - maxSeconds * 1000L + 1);

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
