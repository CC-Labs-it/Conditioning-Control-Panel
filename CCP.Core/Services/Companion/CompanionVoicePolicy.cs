using System;
using System.Collections.Generic;

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
    /// answers something the player did in the app (level up, session end, a click, a chat reply,
    /// a setting, a tongue-out gesture) speaks as before.</para>
    ///
    /// <para>ccp-bugs #1490 #1491 (7.1.13, the idle fix already in): the stings kept coming from
    /// the other lines the companion says on its own while the player is busy elsewhere (a Back
    /// Room run, Discord) with the webcam on: face lost / face found, blink counts, tracking
    /// changes, the away/back state and the window-awareness reactions. They are sensing lines,
    /// not answers, so they follow the same rule (owner, 2026-10-23).</para>
    ///
    /// <para>Classification is by the bark rule's trigger (<see cref="UnpromptedTriggers"/>). The
    /// pack JSON also writes <c>"class": "idle"</c> on most idle rules, but the loader folds that
    /// to Normal, so the trigger is the one signal the code actually carries. The tube's own
    /// awareness reactions (AvatarTubeWindow.Reactions.cs) pass <c>unprompted: true</c>.</para>
    /// </summary>
    public static class CompanionVoicePolicy
    {
        /// <summary>The bark trigger the idle timer raises (and nothing else does).</summary>
        public const string IdleTrigger = "Idle";

        /// <summary>Bark triggers raised by sensing, with no player action in the app behind them.</summary>
        public static readonly IReadOnlySet<string> UnpromptedTriggers = new HashSet<string>(
            new[]
            {
                IdleTrigger, "IdleStateChanged",
                "FaceLost", "FaceFound", "Blink", "TrackingStateChanged",
                "ActivityChanged", "StillOnActivity",
            },
            StringComparer.OrdinalIgnoreCase);

        /// <summary>A bark raised by this trigger is unprompted.</summary>
        public static bool IsUnprompted(string? trigger) =>
            trigger != null && UnpromptedTriggers.Contains(trigger);

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

    }
}
