namespace ConditioningControlPanel.Services.Safety
{
    /// <summary>
    /// How far the voice panic ("red", "safe word", "stop everything") may reach: exactly as far as
    /// the panic key (hard rule 6: no stop gesture is more permissive than the panic key).
    ///
    /// <para>Where the key cannot run (Lockdown holds every key, or the player switched panic off),
    /// the voice panic does nothing either: no preamble, no session pause, no effect stop, no
    /// safety hold. It used to run its whole teardown there ("panic stays unguarded", #706), and
    /// stopping the engine ended a Lockdown with one spoken word (owner, 2026-10-22).</para>
    ///
    /// <para>Panic always works on a leash. Where the key cannot run, a leashed press still runs the
    /// leash part (the task parks, its window closes, the gate stands back; LeashPanicKeyWhilePanicOff),
    /// so a leashed voice panic does the same and nothing more.</para>
    ///
    /// <para>Strict Lock alone does not take the key away (it is the way out of a strict video), so
    /// it does not take the voice panic away. The blink stop is stricter on purpose (owner,
    /// 2026-09-28) and asks <see cref="BlinkStopGate"/>, which shares <see cref="PanicKeyRuns"/>.</para>
    /// </summary>
    internal static class CompatVoicePanicReach
    {
        internal enum Reach
        {
            /// <summary>The whole panic runs: what a panic key press stops.</summary>
            Full,
            /// <summary>The panic cannot run, but a leash task stops (panic always works on a leash).</summary>
            LeashOnly,
            /// <summary>Nothing runs. A word is just a word, as the key is just a key.</summary>
            Refused
        }

        /// <summary>True when a panic key press would run: never under Lockdown, never with panic off.</summary>
        internal static bool PanicKeyRuns(bool lockdownActive, bool panicKeyEnabled) =>
            !lockdownActive && panicKeyEnabled;

        internal static Reach Decide(bool lockdownActive, bool panicKeyEnabled, bool leashed) =>
            PanicKeyRuns(lockdownActive, panicKeyEnabled) ? Reach.Full
            : leashed ? Reach.LeashOnly
            : Reach.Refused;
    }
}
