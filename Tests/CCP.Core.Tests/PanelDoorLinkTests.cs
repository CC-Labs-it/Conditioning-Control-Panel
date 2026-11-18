using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// Declutter wave 2 (2026-11-15, twin of WPF PanelDoorLinkTests): the games left the Play page
/// for the launcher, Just Drop left the Studio strip. Nothing may become a dead end, and the
/// panel reaches the launcher through ONE seam that opens nothing during Lockdown.
/// </summary>
[Collection("PanelDoorLink")]
public class PanelDoorLinkTests
{
    private static void WithSeam(Action body)
    {
        var hook = PanelDoorLink.OpenLauncherDoor;
        var fallback = PanelDoorLink.Fallback;
        var locked = PanelDoorLink.IsLockedDown;
        try { body(); }
        finally
        {
            PanelDoorLink.OpenLauncherDoor = hook;
            PanelDoorLink.Fallback = fallback;
            PanelDoorLink.IsLockedDown = locked;
        }
    }

    [Fact]
    public void TheSeamCallsTheWiredDoorAndFallsBackWhenUnwired() => WithSeam(() =>
    {
        var opened = new List<string>();
        var fellBack = 0;
        PanelDoorLink.IsLockedDown = () => false;
        PanelDoorLink.Fallback = () => fellBack++;

        PanelDoorLink.OpenLauncherDoor = null;
        Assert.True(PanelDoorLink.Open("games"));
        Assert.Equal(1, fellBack);

        PanelDoorLink.OpenLauncherDoor = opened.Add;
        PanelDoorLink.Open("games");
        PanelDoorLink.Open(" Tools ");
        PanelDoorLink.Open("nonsense");
        Assert.Equal(new[] { "games", "tools", "games" }, opened);
        Assert.Equal(1, fellBack);

        // A hook that throws never strands the click: the fallback still opens the launcher.
        PanelDoorLink.OpenLauncherDoor = _ => throw new InvalidOperationException();
        Assert.True(PanelDoorLink.Open("games"));
        Assert.Equal(2, fellBack);

        // Nothing wired on the head: nothing opens, and the caller hears so.
        PanelDoorLink.OpenLauncherDoor = null;
        PanelDoorLink.Fallback = null;
        Assert.False(PanelDoorLink.Open("games"));
    });

    [Fact]
    public void DuringLockdownTheSeamOpensNothing() => WithSeam(() =>
    {
        // AGENTS.md rule 6: the launcher's way back reports an escape attempt to Lockdown.
        var opened = new List<string>();
        var fellBack = 0;
        PanelDoorLink.Fallback = () => fellBack++;
        PanelDoorLink.IsLockedDown = () => true;

        PanelDoorLink.OpenLauncherDoor = opened.Add;
        Assert.False(PanelDoorLink.Open("games"));
        PanelDoorLink.OpenLauncherDoor = null;
        Assert.False(PanelDoorLink.Open("tools"));
        Assert.Empty(opened);
        Assert.Equal(0, fellBack);

        // A check that throws counts as locked: when in doubt, never trip the wire.
        PanelDoorLink.OpenLauncherDoor = opened.Add;
        PanelDoorLink.IsLockedDown = () => throw new InvalidOperationException();
        Assert.False(PanelDoorLink.Open("games"));
        Assert.Empty(opened);
        Assert.Equal(0, fellBack);
    });

    [Fact]
    public void PlayLosesTheGamesPillAndLandsOnEyes()
    {
        var play = NavSections.Find(NavSections.Play)!;
        Assert.Equal("playeyes", play.DefaultTab);
        // 7.3.1 (ccp-bugs #1633): a Games pill is back, last, as a WINDOW pill that opens the launcher's Games door.
        Assert.Equal(new[] { "playeyes", "playsessions", "deeper", "play" },
            play.Tabs.Where(t => !t.Hidden).Select(t => t.Key).ToArray());
        Assert.Equal(NavTabKind.Window, play.Tabs.Single(t => t.Key == "play").Kind);
        Assert.Equal(new[] { "playeyes", "playsessions", "deeper", "play" }, NavStripTable.Pills(NavSections.Play).Select(t => t.Key).ToArray());
    }

    [Fact]
    public void JustDropHasNoPillButStaysOwnedByStudio()
    {
        Assert.DoesNotContain(NavStripTable.Pills(NavSections.Studio), t => t.Key == "justdrop");
        var jd = NavSections.AllTabs.Single(t => t.Key == "justdrop");
        Assert.True(jd.Hidden);
        Assert.Equal(NavTabKind.Window, jd.Kind);
        Assert.Equal(NavSections.Studio, NavSections.SectionForTab("justdrop"));
    }

    [Theory]
    [InlineData("play", "games")]
    [InlineData("PLAY", "games")]
    [InlineData("playeyes", null)]
    [InlineData("lab", null)]
    [InlineData("justdrop", null)]
    [InlineData(null, null)]
    [InlineData("", null)]
    public void OnlyTheOldGamesKeyOpensALauncherDoor(string? tab, string? door)
        => Assert.Equal(door, NavSections.LauncherDoorFor(tab));

    [Fact]
    public void OldKeysLandOnPlayEyesAndLightIt()
    {
        Assert.Equal((NavSections.Play, "playeyes"), NavSections.Redirects["lab"]);
        Assert.Equal((NavSections.Play, "playeyes"), NavSections.Redirects["play"]);
        Assert.Equal(NavSections.Play, NavSections.SectionForTab("play"));
        Assert.Equal("playeyes", NavStripTable.ActivePill("lab"));
        // 7.3.1: "play" is the Games window pill now (it opens the launcher, never a page of its own).
        Assert.Equal(NavTabKind.Window, NavStripTable.Pills(NavSections.Play).Single(t => t.Key == "play").Kind);
        // A remembered old Games pill falls back to Play's landing, never a dead page.
        Assert.Equal("playeyes", NavStripTable.LastTabFor("{\"play\":\"play\"}", NavSections.Play));
        Assert.Equal("playsessions", NavStripTable.LastTabFor("{\"play\":\"playsessions\"}", NavSections.Play));
    }

    [Theory]
    [InlineData("playeyes", false, "top")]      // rail door / default landing: the page from its top
    [InlineData("playeyes", true, "eyes")]      // a click on the Eyes pill still scrolls to Eyes
    [InlineData("PlayEyes", false, "top")]
    [InlineData("playsessions", false, "sessions")]
    [InlineData("playsessions", true, "sessions")]
    [InlineData("deeper", true, null)]
    [InlineData("play", false, null)]
    [InlineData(null, false, null)]
    public void PlayShowsItsTopUnlessAZonePillAsked(string? tab, bool fromPill, string? scroll)
        => Assert.Equal(scroll, NavSections.PlayLandingScroll(tab, fromPill));

    [Fact]
    public void OnlyAnArrivalThatCameForTheGamesEarnsTheMovedNote()
    {
        Assert.True(NavSections.CameForGames("play"));
        Assert.False(NavSections.CameForGames("playeyes"));
        Assert.False(NavSections.CameForGames("playsessions"));
        Assert.False(NavSections.CameForGames("deeper"));
        Assert.False(NavSections.CameForGames(null));
    }

    [Fact]
    public void ThePaletteGamesRowNamesNoPlayZoneAndThePlayDoorLandsOnEyes()
    {
        var all = SettingsPaletteIndex.All;
        var games = all.Single(e => e.Id == "tab.play");
        Assert.Equal("play", games.TabKey);
        Assert.Null(games.PlayZone);
        Assert.DoesNotContain(all, e => e.PlayZone == "games");
        // "play" is filed as a redirect for the chrome, but it opens a launcher door: still searchable.
        Assert.Contains(SettingsPaletteIndex.Search("games"), e => e.Id == "tab.play");
        Assert.Equal("playeyes", all.Single(e => e.Id == "door.play").TabKey);
    }
}
