using System;
using System.Threading;

namespace ConditioningControlPanel.Services.Companion;

/// <summary>WPF CompanionIdleVoice: unprompted chatter keeps its text while the engine is off,
/// but makes sound only during an engine/unpaused session or with the user's explicit opt-in.</summary>
public static class CompanionIdleVoicePolicy
{
    // WPF PanicAftermath's monotonic five-minute quiet, scoped here to the idle voice added by this lane.
    private static long _quietUntilMs;
    internal static long PanicQuietUntilMs
    {
        get => Interlocked.Read(ref _quietUntilMs);
        set => Interlocked.Exchange(ref _quietUntilMs, value);
    }
    public static bool PanicQuiet => IsPanicQuiet(Environment.TickCount64, Interlocked.Read(ref _quietUntilMs));
    public static bool IsPanicQuiet(long nowMs, long untilMs) => untilMs > 0 && nowMs < untilMs;
    public static void ArmPanicQuiet() => Interlocked.Exchange(ref _quietUntilMs, Environment.TickCount64 + 300000);
    public static void EndPanicQuiet() => Interlocked.Exchange(ref _quietUntilMs, 0);

    public static bool ShouldVoice(bool unprompted, bool engineRunning, bool sessionRunning,
        bool sessionPaused, bool speakIdleWhenEngineOff, bool panicQuiet) =>
        !unprompted || (!panicQuiet && (engineRunning || (sessionRunning && !sessionPaused) || speakIdleWhenEngineOff));
}
