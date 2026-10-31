using System;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>WPF Services/Chaos/DtrhHostService.cs:959-981 StartHeartbeatWatch: a loading page cannot false-trip; a live
/// descent gets the fast ladder, the idling Warren a slower one. No ping grace on this host.</summary>
public static class DtrhHeartbeatRule
{
    public const int PollSeconds = 5;

    public static bool ShouldRecover(bool ready, bool exiting, bool runActive, TimeSpan silence)
        => ready && !exiting && silence.TotalSeconds > (runActive ? 10 : 20);
}
