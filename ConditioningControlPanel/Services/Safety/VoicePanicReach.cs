namespace ConditioningControlPanel.Services.Safety
{
    /// <summary>
    /// How far the voice panic ("red", "safe word", "stop everything") may reach.
    ///
    /// <para>Hard rule 6: no stop gesture is more permissive than the panic key. Where the key cannot
    /// run (Lockdown holds every key, or the player switched panic off), the key never stops the
    /// engine, so the voice panic must not either. Stopping it there ended a Lockdown with one spoken
    /// word, and the panic hold (PanicAftermath) then kept the Lockdown keeper from starting it again
    /// until the player pressed START.</para>
    ///
    /// <para>The effect stop the voice panic always did is left as it was. Refusing the voice panic
    /// there entirely, as BlinkStopGate refuses the blink stop, is an owner call.</para>
    /// </summary>
    internal static class VoicePanicReach
    {
        /// <summary>True when the voice panic may stop the engine: only where the panic key could.</summary>
        internal static bool StopsEngine(bool lockdownActive, bool panicKeyEnabled) =>
            !lockdownActive && panicKeyEnabled;
    }
}
