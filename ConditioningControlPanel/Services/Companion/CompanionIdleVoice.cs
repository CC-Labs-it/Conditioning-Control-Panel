using System;

namespace ConditioningControlPanel.Services.Companion
{
    /// <summary>
    /// ccp-bugs #1468 #1469 #1471 #1472 ("random audio stings" with the engine off). The 7.1.7
    /// engine-off trace showed most of them were the companion's idle lines: the Idle bark,
    /// fired by the tube's idle timer about once a minute with nobody doing anything.
    ///
    /// <para>The rule: an UNPROMPTED line (fired on the idle timer, no player action behind it)
    /// keeps its bubble and text but plays no sound while neither the engine nor a session runs,
    /// unless the player turned <c>CompanionSpeaksIdleWhenEngineOff</c> on. Every line that
    /// answers something the player did (level up, session end, a click, a chat reply, a setting,
    /// the webcam reactions) speaks as before.</para>
    ///
    /// <para>Classification is by the bark rule's trigger: <see cref="IdleTrigger"/> is raised
    /// only by <c>BarkService.DispatchIdle</c>, the idle timer. The pack JSON also writes
    /// <c>"class": "idle"</c> on most of those rules, but the loader folds that to Normal, so
    /// the trigger is the one signal the code actually carries.</para>
    /// </summary>
    public static class CompanionIdleVoice
    {
        /// <summary>The bark trigger the idle timer raises (and nothing else does).</summary>
        public const string IdleTrigger = "Idle";

        /// <summary>A bark raised by this trigger is unprompted.</summary>
        public static bool IsUnprompted(string? trigger) =>
            string.Equals(trigger, IdleTrigger, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Pure rule: may this line make a sound? Prompted lines always may; unprompted lines
        /// only while the engine or a session runs, or when the player opted in.
        /// </summary>
        public static bool ShouldVoice(bool unprompted, bool engineRunning, bool speakIdleWhenEngineOff) =>
            !unprompted || engineRunning || speakIdleWhenEngineOff;

        /// <summary>
        /// ccp-bugs #1464 stack review: the panic aftermath. An unprompted line never makes a sound
        /// inside the post-panic quiet (Safety.PanicAftermath), opted in or not, and a session a
        /// panic PAUSED does not count as running - a panic-paused session kept IsSessionRunning
        /// true, and a remote panic never stops the engine. Prompted lines are untouched.
        /// </summary>
        public static bool ShouldVoice(bool unprompted, bool engineRunning, bool sessionRunning, bool sessionPaused,
                                       bool speakIdleWhenEngineOff, bool panicQuiet) =>
            !unprompted
            || (!panicQuiet && ShouldVoice(unprompted, engineRunning || (sessionRunning && !sessionPaused), speakIdleWhenEngineOff));

        /// <summary><see cref="ShouldVoice(bool, bool, bool, bool, bool, bool)"/> against the live app
        /// state (a prompted line never reads it).</summary>
        public static bool ShouldVoiceNow(bool unprompted) =>
            !unprompted || ShouldVoice(unprompted, App.IsEngineRunning, App.IsSessionRunning,
                SessionEngine.Active?.IsPaused == true,
                App.Settings?.Current?.CompanionSpeaksIdleWhenEngineOff == true,
                Safety.PanicAftermath.TriggersSilenced());
    }
}
