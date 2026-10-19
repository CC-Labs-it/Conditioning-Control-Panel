using System;
using System.Threading;

namespace ConditioningControlPanel.Services.Safety
{
    /// <summary>
    /// What a panic leaves standing after its stop pass, so nothing creeps back on its own.
    /// Found on ccp-bugs #1464, where the log showed two leaks against "panic stops everything":
    ///
    /// <para>1. Trigger Mode runs on the companion tube's own timer, outside the engine. After a
    /// haptics PANIC STOP it kept speaking triggers and pulsing the toy about 22 s later. A panic
    /// now arms a QUIET window (the same five minutes EMI keeps silent after a panic). Trigger
    /// Mode skips its tick inside it, haptics included, muted or not. A user's START ends it.</para>
    ///
    /// <para>2. A remote "resume" (or the voice "resume", same path) resumed a session the LOCAL
    /// panic had paused, and ResumeSession restarts flashes, video and subliminals even with the
    /// engine stopped. The pause a panic made is remembered; a remote resume of THAT pause is
    /// refused. Any later pause (the session was resumed locally and paused again) is a normal
    /// one again, so nothing has to remember to clear it.</para>
    ///
    /// Pure rules plus one process-wide quiet stamp. Never weakens a panic: both only ever refuse.
    /// </summary>
    public static class PanicAftermath
    {
        /// <summary>The post-panic quiet: the length of EMI's panicPressed hold tail (300 s).</summary>
        public static readonly TimeSpan QuietWindow = TimeSpan.FromMinutes(5);

        private static long _quietUntilUtcTicks;

        public static DateTime QuietUntil(DateTime panicUtc) => panicUtc + QuietWindow;

        public static bool IsQuiet(DateTime nowUtc, DateTime quietUntilUtc) => nowUtc < quietUntilUtc;

        /// <summary>A panic press, any rung. Safe from any thread.</summary>
        public static void ArmQuiet(DateTime nowUtc) =>
            Interlocked.Exchange(ref _quietUntilUtcTicks, QuietUntil(nowUtc).Ticks);

        /// <summary>The user started the engine again: what they stopped may come back.</summary>
        public static void EndQuiet() => Interlocked.Exchange(ref _quietUntilUtcTicks, 0);

        /// <summary>TRUE while Trigger Mode must stay silent (no bubble, no audio, no haptics).</summary>
        public static bool TriggersSilenced(DateTime nowUtc) =>
            IsQuiet(nowUtc, new DateTime(Interlocked.Read(ref _quietUntilUtcTicks), DateTimeKind.Utc));

        /// <summary>The pause a panic left on a session: which session, and which pause of it.</summary>
        public readonly record struct PanicPause(object Session, int PauseCount);

        /// <summary>The mark to keep after a panic, or null when no session sits paused.</summary>
        public static PanicPause? MarkPause(object? session, bool sessionRunning, bool sessionPaused, int pauseCount) =>
            session != null && sessionRunning && sessionPaused ? new PanicPause(session, pauseCount) : null;

        /// <summary>
        /// TRUE when a remote resume would undo the local panic: the same session is still in the
        /// very pause the panic left. Only a local START or Resume picks that one up.
        /// </summary>
        public static bool RemoteResumeRefused(PanicPause? mark, object? session, bool sessionPaused, int pauseCount) =>
            mark is { } m && sessionPaused && session != null
            && ReferenceEquals(m.Session, session) && m.PauseCount == pauseCount;
    }
}
