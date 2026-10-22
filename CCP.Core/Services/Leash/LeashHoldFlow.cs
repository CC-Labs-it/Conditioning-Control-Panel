using System;

namespace ConditioningControlPanel.Services.Leash;

/// <summary>What the head does after one panic-key event of a hold (WPF MainWindow.Leash.cs:311-351).
/// <paramref name="Swallow"/> = a repeat, never a new panic press. <paramref name="Ask"/> = the hold
/// just reached five seconds while leashed: ask "Cut the leash?" (never cut here). <paramref name="Ring"/>
/// = show the ring for this much hold (only when its number changes). <paramref name="Tick"/> = the
/// second to tick. <paramref name="Dismiss"/> = take the ring down.</summary>
public readonly record struct LeashHoldStep(bool Swallow, bool Ask, TimeSpan? Ring, int? Tick, bool Dismiss);

/// <summary>
/// The hold-to-cut flow over <see cref="LeashHoldToCut"/>, ported from WPF LeashHoldSwallows and
/// OnLeashKeyReleased. A head without key auto-repeat (X11 raw keys) feeds <see cref="Down"/> from a
/// short clock while the key is down; that is a repeat by <see cref="LeashHoldToCut.RepeatGap"/>.
/// Releasing before five seconds only dismisses: nothing here ever cuts. Pure; one thread.
/// </summary>
public sealed class LeashHoldFlow
{
    private readonly LeashHoldToCut _hold = new();
    private int _ticked, _shown;

    /// <summary>True while a hold is being timed (the key is down).</summary>
    public bool Held => _hold.Held;

    public LeashHoldStep Down(DateTime now, bool leashed)
    {
        var (repeat, due) = _hold.Down(now, leashed);
        if (!repeat) { _ticked = 0; _shown = 0; }
        if (due) return new(repeat, Ask: true, Ring: null, Tick: null, Dismiss: true);
        TimeSpan? ring = null;
        int? tick = null;
        // The ring and the tick only ride a repeat while leashed, never the first down (the real panic press).
        if (leashed && repeat && _hold.HeldFor(now) is { } held)
        {
            var left = LeashHoldTick.SecondsLeft(held);
            if (left != _shown) { _shown = left; ring = held; }
            if (LeashHoldTick.Due(held, _ticked) is int sec) { _ticked = sec; tick = sec; }
        }
        return new(repeat, false, ring, tick, false);
    }

    /// <summary>The key came up, or the leash went off: the hold ends and the ring goes.</summary>
    public LeashHoldStep Up()
    {
        _hold.Up();
        _shown = 0;
        return new(false, false, null, null, Dismiss: true);
    }
}
