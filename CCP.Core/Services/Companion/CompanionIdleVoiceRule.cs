using System;
using ConditioningControlPanel.Services.Safety;

namespace ConditioningControlPanel.Services.Companion;

/// <summary>WPF CompanionIdleVoice.cs:28-83. Keep unprompted text while holding its audio.</summary>
public static class CompanionIdleVoiceRule
{
    public const string IdleTrigger = "Idle";
    public static bool IsUnprompted(string? trigger) => trigger != null &&
        (trigger.Equals(IdleTrigger, StringComparison.OrdinalIgnoreCase)
         || trigger.Equals("IdleStateChanged", StringComparison.OrdinalIgnoreCase)
         || trigger.Equals("FaceLost", StringComparison.OrdinalIgnoreCase)
         || trigger.Equals("FaceFound", StringComparison.OrdinalIgnoreCase)
         || trigger.Equals("Blink", StringComparison.OrdinalIgnoreCase)
         || trigger.Equals("TrackingStateChanged", StringComparison.OrdinalIgnoreCase)
         || trigger.Equals("ActivityChanged", StringComparison.OrdinalIgnoreCase)
         || trigger.Equals("StillOnActivity", StringComparison.OrdinalIgnoreCase));

    public static bool ShouldVoice(bool unprompted, bool engineRunning, bool speakIdleWhenEngineOff) =>
        !unprompted || engineRunning || speakIdleWhenEngineOff;

    public static bool ShouldVoice(bool unprompted, bool engineRunning, bool sessionRunning,
        bool sessionPaused, bool speakIdleWhenEngineOff, bool panicQuiet) =>
        !unprompted || (!panicQuiet && ShouldVoice(true,
            engineRunning || (sessionRunning && !sessionPaused), speakIdleWhenEngineOff));

    public static bool ShouldVoiceNow(bool unprompted) => !unprompted || ShouldVoice(true,
        CoreSession.IsEngineRunning, CoreSession.IsSessionRunning, CoreSession.IsSessionPaused,
        CoreSettings.Current.CompanionSpeaksIdleWhenEngineOff, PanicAftermath.TriggersSilenced());
}
