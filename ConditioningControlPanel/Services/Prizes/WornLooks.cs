using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Prizes;

/// <summary>
/// THE WORN LOOKS: the one place the desktop asks "what is worn in slot X" for the screen effects the
/// shop sells (bubble skin, Deck card frame, the word behind the card, fog colour, the two-tone mix and its
/// second colour, word style). Answers counter row ids (<c>fx.bubble.champagne</c>); the effect asks at
/// the moment it starts something new and may listen to <see cref="Changed"/> to restyle what is on screen.
///
/// <para>Today only the DEBUG shop preview feeds it (<see cref="Source"/>); without it every slot reads
/// its free default, which is exactly today's look. The synced account equip is a later lane and plugs
/// in here, so no effect needs to change again.</para>
/// </summary>
public static class WornLooks
{
    public const string Bubble = "bubble", Card = "card", Behind = "behind", Fog = "fog", Fog2 = "fog2", Mix = "mix", Word = "word";
    public const string BubblePrefix = "fx.bubble.";

    /// <summary>The five slots that wear one row each, in shelf order.</summary>
    public static readonly IReadOnlyList<string> EffectSlots = new[] { Bubble, Card, Behind, Fog, Word };

    /// <summary>The free row of a slot (today's look). <see cref="Mix"/> defaults to off (null).</summary>
    public static string? DefaultOf(string? slot) => slot switch
    {
        Bubble => "fx.bubble.soap",
        Card => "fx.card.plain",
        Behind => "fx.behind.none",
        Fog => "fx.fog.pink",
        Fog2 => "fx.fog.lilac",
        Word => "fx.word.classic",
        _ => null,
    };

    /// <summary>Who answers for the slots: (slot) -> row id, or null for "the default". Null = nobody yet.</summary>
    internal static Func<string, string?>? Source { get; set; }

    /// <summary>Raised with the slot name after something was put on or taken off. Any thread.</summary>
    public static event Action<string>? Changed;

    /// <summary>The row worn in <paramref name="slot"/>. For <see cref="Mix"/>: the Two-tone row id while
    /// the mix is on, else null. Never throws.</summary>
    public static string? Get(string slot)
    {
        string? worn = null;
        try { worn = Source?.Invoke(slot); }
        catch (Exception ex) { App.Logger?.Debug("WornLooks: source failed for {Slot}: {E}", slot, ex.Message); }
        return worn ?? DefaultOf(slot);
    }

    public static bool MixOn => Get(Mix) != null;

    /// <summary>The bubble skin id a worn bubble row names (<c>fx.bubble.heart</c> -> <c>heart</c>).
    /// Whether that skin is drawn yet is <c>BubbleSkinChoice.Resolve</c>'s call (unknown = soap).</summary>
    public static string SkinOf(string? rowId)
        => rowId != null && rowId.StartsWith(BubblePrefix, StringComparison.Ordinal) && rowId.Length > BubblePrefix.Length
            ? rowId[BubblePrefix.Length..]
            : BubbleCore.BubbleSkinIds.Soap;

    public static string BubbleSkin() => SkinOf(Get(Bubble));

    internal static void Raise(string slot)
    {
        var listeners = Changed;
        if (listeners == null) return;
        foreach (Action<string> l in listeners.GetInvocationList())
        {
            try { l(slot); }
            catch (Exception ex) { App.Logger?.Debug("WornLooks: a listener threw: {E}", ex.Message); }
        }
    }

    /// <summary>Startup (App): new bubbles take their skin from here (<c>BubbleSkinChoice.Current</c>).</summary>
    public static void Wire() => BubbleCore.BubbleSkinChoice.Worn = BubbleSkin;
}
