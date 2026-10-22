using System;

namespace ConditioningControlPanel.Services.Possession;

/// <summary>The head-free rules of the reactive adapter (WPF PossessionEvents): which setting writes
/// the room may answer, and which buttons count as Start / Stop. The throttle, rung gate and cooldowns
/// are the director's (<see cref="PossessionDirector.RequestReactive"/>); the head only watches input.</summary>
public static class PossessionReactiveRules
{
    /// <summary>The Possession settings themselves are exempt: reacting to "the user just turned the
    /// haunt down" by haunting them is the one joke that reads as the app ignoring consent. A string
    /// compare, so it is safe on any thread and an exempt setting never pays for a UI hop.</summary>
    public static bool IsConsentSetting(string? propertyName)
    {
        var name = propertyName ?? "";
        return name.StartsWith("LockdownPossession", StringComparison.Ordinal)
               || name.StartsWith("LockdownPhotosafe", StringComparison.Ordinal)
               || name.StartsWith("Gentle", StringComparison.Ordinal)   // Gentle visuals reads as photosafe
               || name.StartsWith("LockdownTripwires", StringComparison.Ordinal)
               || name.StartsWith("LockdownWarden", StringComparison.Ordinal);
    }

    /// <summary>Name, caption or possession tag says Start / Stop. Deliberately loose (the caption is
    /// localized, the x:Name is not) - a false positive costs one dodge on a button that was not the
    /// scary one.</summary>
    public static bool LooksLikeStartStop(string? name, string? caption, string? tag)
        => Says(name) || Says(caption) || Says(tag);

    private static bool Says(string? s)
        => s != null && (s.Contains("start", StringComparison.OrdinalIgnoreCase)
                         || s.Contains("stop", StringComparison.OrdinalIgnoreCase));
}
