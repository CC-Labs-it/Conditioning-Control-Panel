using System;

namespace ConditioningControlPanel.Services.Dares;

/// <summary>
/// Everything a dare does OUTSIDE the wire: the wallet, Circe's tab, the real Lockdown, the account.
/// The app provides the host; tests hand in a fake. Never throws.
/// </summary>
public interface IDareHost
{
    /// <summary>The signed-in unified id, or null.</summary>
    string? Account();

    /// <summary>Minutes east of UTC for the sender's deadline (<c>tz_min</c>).</summary>
    int TzMinutes();

    /// <summary>A Chaster lock can take minutes now: linked, the tab on, not paused, no safety hold.</summary>
    bool ChasterReady();

    /// <summary>Book <paramref name="seconds"/> on the player's own tab (the lost-to-a-player row).
    /// Returns the seconds the tab took (its own limits may take fewer).</summary>
    int BookLockSeconds(int seconds);

    /// <summary>A debited receipt from <c>pay</c>: adopt <paramref name="sp"/> through the wallet rule.</summary>
    void AdoptSp(string account, int sp);

    /// <summary>The player's Sparkles right now, or null when unknown (the server still decides).</summary>
    int? Sparkles() => null;

    bool LockdownActive { get; }

    /// <summary>Starts the REAL Lockdown (the same Activate the Lockdown card calls). False when it did not start.</summary>
    bool StartLockdown(TimeSpan duration);

    /// <summary>A Lockdown ended: how long it was set for and how long it really ran.</summary>
    event Action<TimeSpan, TimeSpan>? LockdownEnded;
}
