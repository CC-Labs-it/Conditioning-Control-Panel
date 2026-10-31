using System;

namespace ConditioningControlPanel.Services.Video;

/// <summary>Uniform millisecond offset leaving a whole capped slice. Unknown lengths start at zero.</summary>
public static class VideoLengthCapRules
{
    public static long StartOffsetMs(int maxSeconds, long durationMs, Random random)
    {
        if (maxSeconds <= 0 || durationMs <= maxSeconds * 1000L) return 0;
        return random.NextInt64(durationMs - maxSeconds * 1000L + 1);
    }
}
