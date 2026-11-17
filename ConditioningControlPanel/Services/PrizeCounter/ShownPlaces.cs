using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.PrizeCounter;

/// <summary>
/// WHERE A BOUGHT THING SHOWS (owner 2026-11-17: "where do I find this profile frame? ... did I just buy nothing?").
/// The fitting room says where a worn item now shows and offers a button that takes the user there: the page sends
/// <c>go {place, id}</c> (CONTRACT "Sheet additions"), the host closes the sheet and opens the place. Pure: the
/// places, which the host can open from a door, and the panel key each lands on. The opening itself is
/// <c>PrizeCounterHostService.GoTo</c>.
/// </summary>
internal static class ShownPlaces
{
    /// <summary>The place keys on the wire (sheet/shown.js names the same).</summary>
    public const string Profile = "profile", Desk = "desk", Race = "race", Chess = "chess",
        Bubbles = "bubbles", Deck = "flash", Words = "subliminal", Fog = "pinkfilter";

    public static readonly IReadOnlyList<string> All = new[] { Profile, Desk, Race, Chess, Bubbles, Deck, Words, Fog };

    /// <summary>The panel tab the profile page is (ShowTab: "discord" IS the Profile tab, "profile" matches no case).</summary>
    public const string ProfileTab = "discord";

    /// <summary>The Studio rack key for an effect look's page (StudioTabView rack keys are API), or null.</summary>
    public static string? RackKey(string? place) => place switch
    {
        Bubbles => "bubbles",
        Deck => "flash",
        Words => "subliminal",
        Fog => "pinkfilter",
        _ => null,
    };

    public static bool IsKnown(string? place) => place != null && All.Contains(place, StringComparer.Ordinal);

    /// <summary>
    /// <c>init.goes</c>: the places this open can take the user to. A game's own sheet (the race, chess, the Back Room,
    /// the dressing room) takes nobody anywhere: the player is in the middle of something there, the line shows alone.
    /// EMI's desk only while the desk is switched on (a summon with it off does nothing).
    /// </summary>
    public static string[] Goes(string? from, bool deskOn)
    {
        if (from != null && PrizeCounterHostService.GameDoors.Contains(from, StringComparer.Ordinal)) return Array.Empty<string>();
        return All.Where(p => p != Desk || deskOn).ToArray();
    }
}
