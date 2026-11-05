using System;
using System.Threading;

namespace ConditioningControlPanel.Services;

/// <summary>WPF AudioService.MarkWhisperAudio: extend the voice floor through the clip plus 250 ms.</summary>
public sealed class WhisperActivity
{
    private readonly Func<DateTime> _now;
    private long _until;
    public WhisperActivity(Func<DateTime>? now = null) => _now = now ?? (() => DateTime.UtcNow);
    public bool IsPlaying => _now().Ticks < Interlocked.Read(ref _until);
    public void Mark(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0) return;
        var until = _now().AddSeconds(seconds + 0.25).Ticks;
        long previous;
        do { previous = Interlocked.Read(ref _until); }
        while (until > previous && Interlocked.CompareExchange(ref _until, until, previous) != previous);
    }
}
