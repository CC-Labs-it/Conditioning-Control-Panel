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

        /// <summary><see cref="ShouldVoice"/> against the live app state.</summary>
        public static bool ShouldVoiceNow(bool unprompted) =>
            ShouldVoice(unprompted, Audio.EngineOffAudioLog.EngineRunning,
                App.Settings?.Current?.CompanionSpeaksIdleWhenEngineOff == true);
    }
}
