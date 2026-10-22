using System;

namespace ConditioningControlPanel.Services.Possession;

// =====================================================================================================
//  POSSESSION - the one answer to "may an effect take this away from the user?".
//  Read Services/Possession/POSSESSION.md ("Hard rules") first.
//
//  poss:Possession.Exclude keeps an element and its SUBTREE out of the deck, and that is the whole
//  story for a victim that is a leaf. It is not the whole story for a CONTAINER: Exclude inherits
//  DOWN, never UP, so a card is perfectly enrollable while the Emergency Exit button and the secret
//  exit box sit inside it. An effect that drops that card off-screen, hides it or turns its hit-
//  testing off takes the exits with it - for 45 s (fall) or two minutes (stealcard) - which is the
//  one thing POSSESSION.md says may never happen.
//
//  So: an element is off limits when it IS excluded, when it CONTAINS anything excluded, or when it
//  (or any ancestor) is named for a room the user has to be able to leave. StealCardEffect has
//  carried this rule privately since wave 2; this is the same rule, shared, so the next effect that
//  hides a victim does not have to rediscover it.
// =====================================================================================================
// The name rule lives here (pure, shared by every head); the WPF visual-tree walk is
// ConditioningControlPanel/Services/Possession/PossessionOffLimitsTree.cs.

public static class PossessionOffLimits
{
    /// <summary>The rooms the user must always be able to leave. Matched case-insensitively against
    /// x:Name, so LockdownCardBorder, BtnEmergencyExit and TxtSecretExit all answer to it.</summary>
    private static readonly string[] _reservedNameTokens = { "lockdown", "emergency", "secret" };

    /// <summary>The name half of the rule, pure so it can be pinned by a test.</summary>
    public static bool IsReservedName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        foreach (var token in _reservedNameTokens)
        {
            if (name!.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }
        return false;
    }
}
