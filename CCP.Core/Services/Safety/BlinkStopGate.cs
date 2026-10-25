namespace ConditioningControlPanel.Services.Safety
{
    /// <summary>
    /// When the rapid 6-blink "stop everything and recalibrate" gesture may fire.
    ///
    /// <para>The gesture is a second, hands-free stop button: it kills audio, stops the engine and
    /// pauses the session (a recalibration stop, not a panic: it never ends a session and leaves the
    /// camera, mic and toys alone; WPF MainWindow.LabTab.cs StopAllForRecalibration). It used to ask nothing but its
    /// own toggle, so it was an escape the user had switched off everywhere else. Tester ticket
    /// 2026-09-27 (Mich): a Blink Trainer session under Lockdown, a few fast blinks, everything
    /// stopped, and Lockdown then refused to resume the paused session. Owner, 2026-09-28: it also
    /// cuts through Strict Lock and through a disabled panic key ("no escape").</para>
    ///
    /// <para>The rule: the gesture is never more permissive than the panic key. Wherever the user
    /// (or Lockdown) has taken the way out away, a blink run is just blinking.</para>
    ///
    /// <para>A leash does NOT take the way out away (owner call, 2026-09-28): panic always works on
    /// a leashed account, so the blink stop does too, and it stops a running leash task the same
    /// way a panic press does (parked, still pending, gate back in ten minutes).</para>
    /// </summary>
    internal static class BlinkStopGate
    {
        /// <summary>Why a blink run was ignored. <see cref="None"/> means the gesture may fire.</summary>
        internal enum Block
        {
            None,
            /// <summary>The Blink Trainer asks for blinks; a fast run of them is the game, not a stop.</summary>
            BlinkTrainer,
            /// <summary>Lockdown is running: a halt leaves a paused session Lockdown will not resume.</summary>
            Lockdown,
            /// <summary>The panic key is off ("no escape"); a hands-free stop must not be the way out the user switched off.</summary>
            NoEscape,
            /// <summary>Strict Lock is on: the user promised not to skip, and the halt skipped for them.</summary>
            StrictLock
        }

        internal static Block Check(bool blinkTrainerRunning, bool lockdownActive,
                                    bool panicKeyEnabled, bool strictLockEnabled)
        {
            if (blinkTrainerRunning) return Block.BlinkTrainer;
            // The panic key's own reach, the same rule the voice panic asks (VoicePanicReach).
            if (!VoicePanicReach.PanicKeyRuns(lockdownActive, panicKeyEnabled))
                return lockdownActive ? Block.Lockdown : Block.NoEscape;
            if (strictLockEnabled) return Block.StrictLock;
            return Block.None;
        }
    }
}
