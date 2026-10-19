using System;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// ccp-bugs #1464, "the timer got stuck at 1:14". A panic press PAUSES a running session and
    /// STOPS the engine (MainWindow.RunPanicStopTail). Pressing Start afterwards ran a bare engine:
    /// every effect came back, the session stayed parked, and its clock sat on the value it had at
    /// the panic for the rest of the run. The next panic found the session already paused, so it
    /// logged no pause at all - the reporter's log tail.
    ///
    /// The rule these helpers pin: a session clock on screen either counts, or says it is paused.
    /// It never shows a frozen value that looks like a running one. Pure, so it can be tested
    /// without a window. Panic is untouched: it still pauses the session and stops the engine.
    /// </summary>
    public static class SessionClockRules
    {
        /// <summary>What a session clock is doing right now.</summary>
        public enum ClockFace
        {
            /// <summary>No session: the clocks give the room back.</summary>
            None,
            /// <summary>The session's own one-second tick drives the clocks.</summary>
            Counting,
            /// <summary>The tick is held: the clocks keep their value and carry the pause mark.</summary>
            Paused,
        }

        public static ClockFace Face(bool sessionRunning, bool sessionPaused) =>
            !sessionRunning ? ClockFace.None
            : sessionPaused ? ClockFace.Paused
            : ClockFace.Counting;

        /// <summary>
        /// The mark every clock of a paused session carries, e.g. " [PAUSED]". Empty while it
        /// counts, so a clock without the mark is always one that moves.
        /// </summary>
        public static string PauseMark(bool sessionPaused, string pausedLabel) =>
            sessionPaused ? " [" + (pausedLabel ?? "").Trim() + "]" : "";

        /// <summary>
        /// TRUE when the user's START press should pick the parked session back up: the engine
        /// really started and a session sits paused under it. Asked only on the user's own START
        /// paths (MainWindow.StartEngineFromUser). The scheduler, the Lockdown keeper and a program
        /// or remote session's engine start never ask, so a panic stays the user's word until the
        /// user takes it back.
        /// </summary>
        public static bool StartResumesParkedSession(bool engineRunning, bool sessionRunning, bool sessionPaused) =>
            engineRunning && sessionRunning && sessionPaused;

        /// <summary>
        /// TRUE when a Resume press has to start the engine as well. A panic stops the engine with
        /// the pause; resuming only the session left its clock counting over an engine that the
        /// START button still called stopped. With the engine off, Resume goes through the user's
        /// START, which resumes the session itself (<see cref="StartResumesParkedSession"/>).
        /// </summary>
        public static bool ResumeStartsEngine(bool engineRunning) => !engineRunning;

        /// <summary>
        /// Runs one step of the session's one-second tick or of its stop. WPF's DispatcherTimer
        /// re-arms only after its Tick handler RETURNS: a step that throws (a feature start, a
        /// progress handler) killed the timer for good while the session still read as running,
        /// which froze every clock on its last value. In the stop, a throwing teardown step skipped
        /// SessionStopped, so nothing reset the clocks. A throwing step is reported and the rest
        /// goes on. FALSE = it threw.
        /// </summary>
        public static bool RunStep(Action step, Action<Exception>? onError)
        {
            try
            {
                step();
                return true;
            }
            catch (Exception ex)
            {
                try { onError?.Invoke(ex); } catch { }
                return false;
            }
        }
    }
}
