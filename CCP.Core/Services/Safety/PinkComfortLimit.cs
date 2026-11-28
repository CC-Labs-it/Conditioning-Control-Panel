using ConditioningControlPanel.Looks;

namespace ConditioningControlPanel.Services.Safety;

/// <summary>
/// Live reads of the Accessibility "Limit pink filter to" switch (rules in CCP.Core
/// <see cref="PinkComfort"/>). The heads clip where the tint is painted: the compositor's
/// <c>PinkTintLayer</c>, the legacy tint windows in OverlayService and the V2 fog in
/// <c>SteamLayer</c>. No settings service (tests, early start): the limit reads as off.
/// </summary>
public static class PinkComfortLimit
{
    public static bool On => CoreSettings.Service?.Current?.PinkComfortLimitOn == true;

    public static int Pct => PinkComfort.ClampPct(CoreSettings.Service?.Current?.PinkComfortLimitPct ?? PinkComfort.DefaultPct);

    /// <summary>The highest opacity (0..1) the tint may paint right now; 1 with the limit off.</summary>
    public static double Cap => PinkComfort.Cap(On, Pct);

    /// <summary>The opacity (0..1) to paint for the one asked for.</summary>
    public static double Apply(double opacity) => PinkComfort.Apply(opacity, On, Pct);

    /// <summary>The V2 fog's thickness to paint for the one asked for.</summary>
    public static double FogStrength(double strength) => PinkComfort.FogStrength(strength, On, Pct);
}
