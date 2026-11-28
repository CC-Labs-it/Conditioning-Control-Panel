namespace ConditioningControlPanel.Services.Super;

/// <summary>Creep speed: how long the fog takes to climb from nothing to the cap. Lives in Core
/// because AppSettings stores it; the fog itself is drawn by each head.</summary>
public enum CreepSpeed
{
    Slow,     // 15 minutes
    Normal,   // 10 minutes
    Fast,     // 5 minutes
}
