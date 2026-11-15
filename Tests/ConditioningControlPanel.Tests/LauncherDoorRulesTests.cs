using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Launcher;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The launcher's doors (declutter wave 2): the right side opens on GAMES and TOOLS, each door a
/// page of the catalogue. The rules are pure so the Avalonia head can copy them.
/// </summary>
public class LauncherDoorRulesTests
{
    private static LauncherDoorCard C(string id, LauncherDoor door = LauncherDoor.Games, bool listed = true,
                                      bool available = true, bool isNew = false, bool locked = false)
        => new(id, door, listed, available, isNew, locked);

    private static readonly LauncherDoorCard[] Cards =
    {
        C("backroom"),
        C("breakoutdemo", listed: false, isNew: true),
        C("breakout", isNew: true),
        C("chess", isNew: true),
        C("hidden", available: false, isNew: true),
        C("editor", LauncherDoor.Tools),
        C("loom", LauncherDoor.Tools),
    };

    [Fact]
    public void A_page_is_its_doors_listed_available_cards_in_order()
    {
        Assert.Equal(new[] { "backroom", "breakout", "chess" }, LauncherDoorRules.Page(Cards, LauncherDoor.Games));
        Assert.Equal(new[] { "editor", "loom" }, LauncherDoorRules.Page(Cards, LauncherDoor.Tools));
        Assert.Equal(3, LauncherDoorRules.Count(Cards, LauncherDoor.Games));
        Assert.Equal(2, LauncherDoorRules.Count(Cards, LauncherDoor.Tools));
        Assert.Empty(LauncherDoorRules.Page(null!, LauncherDoor.Games));
    }

    [Fact]
    public void New_counts_only_unseen_new_cards_on_the_page_and_opening_the_page_clears_it()
    {
        Assert.Equal(2, LauncherDoorRules.NewCount(Cards, LauncherDoor.Games, null));
        Assert.Equal(1, LauncherDoorRules.NewCount(Cards, LauncherDoor.Games, new[] { "BREAKOUT" }));
        Assert.Equal(0, LauncherDoorRules.NewCount(Cards, LauncherDoor.Tools, null));

        var seen = LauncherDoorRules.MarkSeen(Cards, LauncherDoor.Games, new[] { "old", "breakout" });
        Assert.Equal(new[] { "old", "breakout", "chess" }, seen);
        Assert.Equal(0, LauncherDoorRules.NewCount(Cards, LauncherDoor.Games, seen));
        // Marking twice changes nothing, so the stored list never churns.
        Assert.Equal(seen, LauncherDoorRules.MarkSeen(Cards, LauncherDoor.Games, seen));
    }

    [Fact]
    public void Continue_offers_the_last_played_listed_game_and_nothing_else()
    {
        Assert.Equal("chess", LauncherDoorRules.ContinuePick(Cards, "Chess"));
        Assert.Equal("breakout", LauncherDoorRules.ContinuePick(Cards, "breakoutdemo"));   // folded card
        Assert.Null(LauncherDoorRules.ContinuePick(Cards, null));
        Assert.Null(LauncherDoorRules.ContinuePick(Cards, "  "));
        Assert.Null(LauncherDoorRules.ContinuePick(Cards, "gone"));
        Assert.Null(LauncherDoorRules.ContinuePick(Cards, "hidden"));    // not available today
        Assert.Null(LauncherDoorRules.ContinuePick(Cards, "loom"));      // a tool is never continued
        // Access lost since the last run: no button, so no Click sound before a refusal.
        var withLocked = Cards.Append(C("prime", locked: true)).ToArray();
        Assert.Null(LauncherDoorRules.ContinuePick(withLocked, "prime"));
        Assert.Equal("chess", LauncherDoorRules.ContinuePick(withLocked, "chess"));
        Assert.True(LauncherDoorRules.RemembersLaunch(LauncherDoor.Games));
        Assert.False(LauncherDoorRules.RemembersLaunch(LauncherDoor.Tools));
    }

    [Fact]
    public void Door_keys_round_trip_and_an_unknown_key_is_refused()
    {
        foreach (var door in Enum.GetValues<LauncherDoor>())
        {
            Assert.True(LauncherDoorRules.TryParse(LauncherDoorRules.Key(door).ToUpperInvariant(), out var back));
            Assert.Equal(door, back);
        }
        Assert.False(LauncherDoorRules.TryParse("panel", out _));
        Assert.False(LauncherDoorRules.TryParse(null, out _));
    }
}
