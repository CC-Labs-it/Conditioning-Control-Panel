using System.Threading;
using ConditioningControlPanel.Services.Prizes;
using ConditioningControlPanel.SuperCore;

namespace ConditioningControlPanel.Services.Super;

/// <summary>
/// What the Deck wears right now, asked of <see cref="WornLooks"/> once and kept until it says a slot
/// changed (<see cref="WornLooks.Changed"/> may fire on any thread; it only drops the kept answer,
/// and the next frame asks again). Read per frame by the flash layer, so a card already on screen
/// takes a new back on its next flip.
/// </summary>
internal static class DeckLookChoice
{
    private static volatile int _back = -1;   // -1 = ask again
    private static int _changes;

    static DeckLookChoice() => WornLooks.Changed += slot =>
    {
        if (slot != WornLooks.Card) return;
        Interlocked.Increment(ref _changes);
        _back = -1;
    };

    /// <summary>The back every Deck card turns over to. Plain (the default row) = today's card, no back.</summary>
    public static DeckBack Back
    {
        get
        {
            int b = _back;
            if (b >= 0) return (DeckBack)b;
            int seen = Volatile.Read(ref _changes);
            b = (int)DeckLooks.BackOf(WornLooks.Get(WornLooks.Card));
            if (Volatile.Read(ref _changes) == seen) _back = b;   // a change mid-read asks again next frame
            return (DeckBack)b;
        }
    }
}
