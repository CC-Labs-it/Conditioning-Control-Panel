using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Launcher;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// The launcher's doors (declutter wave 2): the right side opens on GAMES and TOOLS, each door a
/// page of the cards. Ported from the WPF LauncherDoorRulesTests; the rules are shared by both heads.
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

    // ---- the shared card table --------------------------------------------------------------

    [Fact]
    public void Games_door_lists_one_Breakout_card_and_the_demo_stays_resolvable()
    {
        var all = LauncherCards.Every.Select(c => c.DoorCard(available: true, locked: false)).ToList();
        var page = LauncherDoorRules.Page(all, LauncherDoor.Games);
        Assert.Contains("breakout", page);
        Assert.DoesNotContain("breakoutdemo", page);
        Assert.Equal("intake", page[^1]);

        var demo = LauncherCards.Find("breakoutdemo");
        Assert.NotNull(demo);
        Assert.False(demo!.Listed);
        Assert.Equal(LauncherDoor.Games, demo.Door);
        // The one card plays the demo for anyone, so it never asks for an account.
        Assert.False(LauncherCards.Find("breakout")!.RequiresAccount);
    }

    [Fact]
    public void Tools_door_holds_the_five_real_tools_and_no_fake_doors()
    {
        var ids = LauncherCards.Tools.Select(c => c.Id).ToList();
        Assert.Equal(new[] { "deepereditor", "sessioneditor", "modcreator", "justdrop", "loom" }, ids);
        Assert.All(LauncherCards.Tools, t =>
        {
            Assert.Equal(LauncherDoor.Tools, t.Door);
            Assert.True(t.Listed);
            Assert.False(t.RequiresAccount);
            Assert.False(t.IsNew);
            Assert.Same(t, LauncherCards.FindAny(t.Id));
            Assert.Equal("launcher_tool_" + t.Id + "_title", t.TitleKey);
            Assert.Equal("launcher_tool_" + t.Id + "_blurb", t.BlurbKey);
            // A tool is never a --game target and never gets a desktop shortcut.
            Assert.Null(LauncherCards.Find(t.Id));
        });
        Assert.False(LauncherDoorRules.OffersShortcut(LauncherDoor.Tools));
        Assert.DoesNotContain(ids, id => id.Contains("remix") || id.Contains("track"));
        Assert.Empty(ids.Intersect(LauncherCards.All.Select(g => g.Id), StringComparer.OrdinalIgnoreCase));
        Assert.Equal(LauncherCards.All.Count + 5, LauncherCards.Every.Count());
    }

    [Fact]
    public void Door_and_tool_keys_ship_in_all_ten_languages()
    {
        var dir = Languages();
        var files = Directory.GetFiles(dir, "*.json");
        Assert.Equal(10, files.Length);
        var keys = new[] { "launcher_door_tools", "launcher_door_count_games", "launcher_door_count_tools",
                           "launcher_door_new", "launcher_door_continue" }
            .Concat(LauncherCards.Tools.SelectMany(t => new[] { t.TitleKey, t.BlurbKey })).ToArray();
        Assert.Equal(15, keys.Length);
        foreach (var f in files)
        {
            var text = File.ReadAllText(f);
            foreach (var k in keys) Assert.True(text.Contains("\"" + k + "\":"), $"{Path.GetFileName(f)} misses {k}");
        }
    }

    [Fact]
    public void Tool_art_ships_under_features()
    {
        var root = Path.GetDirectoryName(Languages())!;
        while (root != null && !Directory.Exists(Path.Combine(root, "Assets", "features"))) root = Path.GetDirectoryName(root);
        Assert.NotNull(root);
        foreach (var t in LauncherCards.Tools.Where(t => t.ArtPath != null))
        {
            Assert.StartsWith("features/", t.ArtPath);
            Assert.True(File.Exists(Path.Combine(root!, "Assets", t.ArtPath!.Replace('/', Path.DirectorySeparatorChar))), $"{t.Id} art missing");
        }
    }

    private static string Languages()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "CCP.Core", "Localization", "Languages")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "CCP.Core", "Localization", "Languages");
    }
}
