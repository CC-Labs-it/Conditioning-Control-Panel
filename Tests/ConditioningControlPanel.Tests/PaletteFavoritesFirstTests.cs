using System;
using System.Linq;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// 7.1.8, player suggestion "add the favorite drawer to every screen": the Home drawer stays on
/// Home, and the empty Ctrl+K box (which opens over every page) leads with the same pins, in the
/// drawer's order, each wearing the handle's star. No new chrome on any page.
/// </summary>
public class PaletteFavoritesFirstTests
{
    private static readonly string[] Pins = { "tab.leash", "game.backroom", "tab.quests" };

    [Fact]
    public void The_empty_box_leads_with_the_pins_in_drawer_order()
    {
        var rows = SettingsPaletteIndex.OpeningRows(Pins, null);
        Assert.Equal(Pins, rows.Take(Pins.Length).Select(e => e.Id).ToArray());
    }

    [Fact]
    public void Recents_follow_the_pins_and_no_row_shows_twice()
    {
        string json = "";
        foreach (var id in new[] { "tab.friends", "tab.leash", "section.monitors" })
            json = SettingsPaletteIndex.PushRecent(json, id);

        var ids = SettingsPaletteIndex.OpeningRows(Pins, json).Select(e => e.Id).ToList();
        // tab.leash is pinned AND recent: it keeps its pin slot and is not repeated.
        Assert.Equal(new[] { "tab.leash", "game.backroom", "tab.quests", "section.monitors", "tab.friends" },
                     ids.Take(5).ToArray());
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        // The authored opening list is still all there after them.
        Assert.All(SettingsPaletteIndex.Search(string.Empty), e => Assert.Contains(e.Id, ids));
    }

    [Fact]
    public void A_pin_the_registry_does_not_know_is_skipped_and_no_pins_changes_nothing()
    {
        var rows = SettingsPaletteIndex.OpeningRows(new[] { "card.arcademy", "", "tab.quests" }, null);
        Assert.Equal("tab.quests", rows[0].Id);
        Assert.DoesNotContain(rows, e => e.Id == "card.arcademy");

        Assert.Equal(SettingsPaletteIndex.Search(string.Empty).Select(e => e.Id),
                     SettingsPaletteIndex.OpeningRows(null, null).Select(e => e.Id));
    }

    [Fact]
    public void A_pinned_row_wears_the_star_and_an_unpinned_one_does_not()
    {
        var entry = SettingsPaletteIndex.ById("tab.quests")!;
        Assert.Equal(SettingsPaletteWindow.PaletteRow.PinnedMark, new SettingsPaletteWindow.PaletteRow(entry, null, pinned: true).WasHint);
        Assert.Equal(string.Empty, new SettingsPaletteWindow.PaletteRow(entry, null).WasHint);
        Assert.Contains('★', SettingsPaletteWindow.PaletteRow.PinnedMark);
    }
}
