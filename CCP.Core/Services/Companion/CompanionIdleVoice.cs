using System;

namespace ConditioningControlPanel.Services.Companion;

/// <summary>Idle lines keep their bubble while the engine is off.</summary>
public static class CompanionIdleVoice
{
    public static bool IsUnprompted(string? trigger) =>
        string.Equals(trigger, "Idle", StringComparison.OrdinalIgnoreCase);

    public static bool ShouldVoice(bool unprompted, bool engineRunning, bool optedIn) =>
        !unprompted || engineRunning || optedIn;

    public static bool ShouldVoiceNow(bool unprompted) =>
        ShouldVoice(unprompted, CoreSession.IsEngineRunning || CoreSession.IsSessionRunning,
            CoreSettings.Current.CompanionSpeaksIdleWhenEngineOff);
}
