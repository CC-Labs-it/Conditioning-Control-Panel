using System;
using System.Threading;

namespace ConditioningControlPanel.Services.Safety
{
    /// <summary>
    /// What a panic leaves standing after its stop pass, so nothing creeps back on its own.
    /// Found on ccp-bugs #1464, where the log showed two leaks against "panic stops everything":
    ///
    /// <para>1. Trigger Mode runs on the companion tube's own timer, outside the engine. After a
    /// haptics PANIC STOP it kept speaking triggers and pulsing the toy about 22 s later. Any panic
    /// (local or remote) now arms a QUIET window, the same five minutes EMI keeps silent after a
    /// panic, measured on the monotonic tick count. Trigger Mode skips its tick and any queued
    /// trigger inside it, haptics included, muted or not. Only the user's own START ends it early
    /// (WPF MainWindow.StartEngineFromUser; Avalonia the Start button, Start normally and Jump right
    /// in): the scheduler, auto-start, a remote or program session start and the Lockdown keeper never do.</para>
    ///
    /// <para>2. A remote "resume" (or the voice "resume", same path) resumed a session a LOCAL panic
    /// had paused, and ResumeSession restarts flashes, video and subliminals even with the engine
    /// stopped. A local panic remembers the pause it left (this run of this session, this pause of
    /// it); a remote resume of THAT pause is refused. A remote panic never marks (owner, 2026-10-19:
    /// the controller may resume its own panic). Any later pause is a normal one again.</para>
    ///
    /// Pure rules plus one process-wide quiet stamp. Never weakens a panic: both only ever refuse.
    ///
    /// <para>In Core since the 7.2 port (same namespace, so the WPF callers are unchanged). The Avalonia
    /// head arms it from its panic key, the PanicSurfaces stop pass (tray, voice, leash gate), the
    /// PanicWatchdog fallback and a controller's trigger_panic (RemoteCommands.StopEffects), and seeds
    /// AiCommandService.PanicQuietProvider / PanicCountProvider in MainShellWindow.StartPanicKey.</para>
    /// </summary>
    public static class PanicAftermath
    {
        /// <summary>The post-panic quiet: the length of EMI's panicPressed hold tail (300 s).</summary>
        public static readonly TimeSpan QuietWindow = TimeSpan.FromMinutes(5);

        /// <summary>Environment.TickCount64 value the quiet lasts until; 0 = not armed.</summary>
        private static long _quietUntilMs;

        public static long QuietUntil(long panicMs) => panicMs + (long)QuietWindow.TotalMilliseconds;

        public static bool IsQuiet(long nowMs, long quietUntilMs) => quietUntilMs > 0 && nowMs < quietUntilMs;

        /// <summary>1 while no system path may restart the engine (the Lockdown dose keeper).</summary>
        private static int _engineHeld;

        /// <summary>A panic press, any rung but the Ctrl+K palette's, local or remote. Arms the quiet
        /// AND holds engine restarts until the user's START. Safe from any thread.</summary>
        public static void ArmQuiet() => ArmQuiet(Environment.TickCount64);

        public static void ArmQuiet(long nowMs)
        {
            Interlocked.Exchange(ref _quietUntilMs, QuietUntil(nowMs));
            Interlocked.Exchange(ref _engineHeld, 1);
            Interlocked.Increment(ref _panics);
        }

        /// <summary>How many panics this process has seen. An AI follow-up remembers the count when it
        /// is scheduled and never fires once a panic overtook it (AiCommandAdmission).</summary>
        public static long PanicCount => Interlocked.Read(ref _panics);

        private static long _panics;

        /// <summary>The user's own START: what they stopped may come back.</summary>
        public static void EndQuiet()
        {
            Interlocked.Exchange(ref _quietUntilMs, 0);
            Interlocked.Exchange(ref _engineHeld, 0);
        }

        /// <summary>
        /// TRUE from a panic until the user's START (no time limit): the Lockdown dose keeper must not
        /// start the engine again on its own 4 s grace. Only the user undoes a panic.
        /// </summary>
        public static bool HoldsEngineRestart => Volatile.Read(ref _engineHeld) == 1;

        /// <summary>The user switched Lockdown on: a fresh commitment, so the keeper may run the engine
        /// again. Leaves the Trigger Mode quiet alone.</summary>
        public static void ReleaseEngineHold() => Interlocked.Exchange(ref _engineHeld, 0);

        /// <summary>TRUE while Trigger Mode must stay silent (no bubble, no audio, no haptics).</summary>
        public static bool TriggersSilenced() => TriggersSilenced(Environment.TickCount64);

        public static bool TriggersSilenced(long nowMs) => IsQuiet(nowMs, Interlocked.Read(ref _quietUntilMs));

        /// <summary>The pause a local panic left: which session object, which run of it (session
        /// objects are reused across runs), and which pause of that run.</summary>
        public readonly record struct PanicPause(object Session, int Run, int PauseCount);

        /// <summary>The mark a LOCAL panic keeps, or null when no session sits paused.</summary>
        public static PanicPause? MarkPause(object? session, int run, bool sessionRunning, bool sessionPaused, int pauseCount) =>
            session != null && sessionRunning && sessionPaused ? new PanicPause(session, run, pauseCount) : null;

        /// <summary>
        /// TRUE when a remote resume would undo a local panic: the same run of the same session is
        /// still in the very pause the panic left. Only a local START or Resume picks that one up.
        /// </summary>
        public static bool RemoteResumeRefused(PanicPause? mark, object? session, int run, bool sessionPaused, int pauseCount) =>
            mark is { } m && sessionPaused && session != null
            && ReferenceEquals(m.Session, session) && m.Run == run && m.PauseCount == pauseCount;
    }
}
