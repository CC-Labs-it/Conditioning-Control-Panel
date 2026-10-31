using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Companion;

/// <summary>Unprompted lines keep their bubble while their audio is held.</summary>
public static class CompanionIdleVoice
{
    public const string IdleTrigger = "Idle";
    public static readonly IReadOnlySet<string> UnpromptedTriggers = new HashSet<string>(
        new[] { IdleTrigger, "IdleStateChanged", "FaceLost", "FaceFound", "Blink",
            "TrackingStateChanged", "ActivityChanged", "StillOnActivity" }, StringComparer.OrdinalIgnoreCase);

    public static bool IsUnprompted(string? trigger) =>
        trigger != null && UnpromptedTriggers.Contains(trigger);

    public static bool ShouldVoice(bool unprompted, bool engineRunning, bool optedIn) =>
        !unprompted || engineRunning || optedIn;

    public static bool ShouldVoice(bool unprompted, bool engineRunning, bool sessionRunning,
        bool sessionPaused, bool optedIn, bool panicQuiet) =>
        !unprompted || (!panicQuiet && ShouldVoice(unprompted,
            engineRunning || (sessionRunning && !sessionPaused), optedIn));

    public static bool ShouldVoiceNow(bool unprompted) =>
        !unprompted || ShouldVoice(unprompted, CoreSession.IsEngineRunning, CoreSession.IsSessionRunning,
            CoreSession.IsSessionPaused, CoreSettings.Current.CompanionSpeaksIdleWhenEngineOff,
            Safety.PanicAftermath.TriggersSilenced());
}
