using System;

namespace ConditioningControlPanel.Services.Prizes;

/// <summary>
/// BANK ON RETURN, as a rule. A gain that could not fly (no wallet window in front: a game, the
/// Back Room or another program had the screen) is owed to the eye; when the launcher or the
/// panel next comes to the front, what is owed flies as ONE flight for the sum.
///
/// <para>Only the sum is kept, against the settings object it was earned on (another profile
/// drops it). Never owed: a sign-in or profile load (the first fill of an empty wallet), a sync
/// adoption, a reset. Any loss meanwhile (a buy, a stake, a sync that lowered the balance) takes
/// off what is owed, never below zero, and nothing is ever owed beyond the balance itself. Kept in
/// memory only: nothing is owed across a restart. Presentation only: it never writes the wallet.</para>
/// </summary>
public sealed class SparkleOwedBook
{
    private object? _owner;
    private int _owed;

    /// <summary>What is owed now (0 = nothing).</summary>
    public int Owed => _owed;

    /// <summary>The balance at the last change booked, so a cue can tell its own credit.</summary>
    public int Balance { get; private set; }

    /// <summary>Pure: may this gain be owed when it could not fly?</summary>
    public static bool CanOwe(SparkleChange c)
        => c.IsGain && c.Before != 0 && c.Source is not (SparkleSource.Synced or SparkleSource.Reset);

    /// <summary>Books one change of <paramref name="owner"/>'s balance. <paramref name="shown"/> =
    /// a wallet window in front took it (a flight, or Motion Off's bell): nothing is owed for it.</summary>
    public void Note(object owner, SparkleChange c, bool shown)
    {
        if (owner == null) return;
        if (!ReferenceEquals(owner, _owner)) { _owner = owner; _owed = 0; }
        Balance = c.Balance;
        if (c.Source == SparkleSource.Reset) { _owed = 0; return; }
        if (c.Amount < 0) _owed = Math.Max(0, _owed + c.Amount);
        else if (!shown && CanOwe(c)) _owed = (int)Math.Min(int.MaxValue, (long)_owed + c.Amount);
        _owed = Math.Clamp(_owed, 0, Math.Max(0, c.Balance));
    }

    /// <summary>What is owed against <paramref name="owner"/> standing at <paramref name="balance"/>,
    /// without taking it (0 for another profile).</summary>
    public int OwedTo(object? owner, int balance)
        => owner != null && ReferenceEquals(owner, _owner) ? Math.Clamp(_owed, 0, Math.Max(0, balance)) : 0;

    /// <summary>Takes what is owed: the number the stars start from and the one they end on.</summary>
    public (int From, int To)? Take(object? owner, int balance)
    {
        var owed = OwedTo(owner, balance);
        _owed = 0;
        return owed > 0 ? (balance - owed, balance) : null;
    }

    /// <summary>Panic, a closed panel: nothing is owed any more.</summary>
    public void Clear() => _owed = 0;
}
