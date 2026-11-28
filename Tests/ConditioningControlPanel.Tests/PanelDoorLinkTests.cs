using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Controls.NavRail;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Declutter wave 2 (2026-11-15): the games left the Play page for the launcher, Just Drop left
/// the Studio strip and the Deeper Editor tile left Home. Nothing may become a dead end: every
/// old key still lands somewhere, and the panel reaches the launcher through ONE seam.
/// </summary>
public class PanelDoorLinkTests
{
    private static string AppFile(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "ConditioningControlPanel", "Services")))
            dir = Path.GetDirectoryName(dir);
        Assert.True(dir != null, "repo root not found");
        return File.ReadAllText(Path.Combine(new[] { dir!, "ConditioningControlPanel" }.Concat(parts).ToArray()));
    }

    [Fact]
    public void TheSeamCallsTheWiredDoorAndFallsBackWhenUnwired()
    {
        var hook = PanelDoorLink.OpenLauncherDoor;
        var fallback = PanelDoorLink.Fallback;
        var locked = PanelDoorLink.IsLockedDown;
        try
        {
            var opened = new List<string>();
            var fellBack = 0;
            PanelDoorLink.IsLockedDown = () => false;
            PanelDoorLink.Fallback = () => fellBack++;

            PanelDoorLink.OpenLauncherDoor = null;
            PanelDoorLink.Open("games");
            Assert.Equal(1, fellBack);

            PanelDoorLink.OpenLauncherDoor = opened.Add;
            PanelDoorLink.Open("games");
            PanelDoorLink.Open(" Tools ");
            PanelDoorLink.Open("nonsense");
            Assert.Equal(new[] { "games", "tools", "games" }, opened);
            Assert.Equal(1, fellBack);

            // A hook that throws never strands the click: the fallback still opens the launcher.
            PanelDoorLink.OpenLauncherDoor = _ => throw new InvalidOperationException();
            PanelDoorLink.Open("games");
            Assert.Equal(2, fellBack);
        }
        finally
        {
            PanelDoorLink.OpenLauncherDoor = hook;
            PanelDoorLink.Fallback = fallback;
            PanelDoorLink.IsLockedDown = locked;
        }
    }

    [Theory]
    [InlineData("playeyes", false, "top")]      // rail door / default landing: the page from its title
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
    public void OnlyAnArrivalThatCameForTheGamesSpendsTheMovedNote()
    {
        Assert.True(NavSections.CameForGames("play"));
        Assert.False(NavSections.CameForGames("playeyes"));
        Assert.False(NavSections.CameForGames("playsessions"));
        Assert.False(NavSections.CameForGames("deeper"));
        Assert.False(NavSections.CameForGames(null));

        var nav = AppFile("MainWindow", "MainWindow.TabNavigation.cs");
        Assert.Contains("if (_playCameForGames)", nav, StringComparison.Ordinal);
        Assert.Contains("NavSections.PlayLandingScroll(tab, _playZoneFromPill)", nav, StringComparison.Ordinal);
        Assert.Contains("NavSections.CameForGames(NavRailRules.ReadLastTab(section, json))",
            AppFile("MainWindow", "MainWindow.NavRail.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void DuringLockdownTheSeamOpensNothing()
    {
        // AGENTS.md rule 6: the launcher's way back reports an escape attempt to Lockdown, so a
        // Drop-day card or a palette row reaching the seam mid-Lockdown must not get that far.
        var hook = PanelDoorLink.OpenLauncherDoor;
        var fallback = PanelDoorLink.Fallback;
        var locked = PanelDoorLink.IsLockedDown;
        try
        {
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
            PanelDoorLink.IsLockedDown = () => throw new InvalidOperationException();
            Assert.False(PanelDoorLink.Open("games"));
            Assert.Equal(0, fellBack);

            PanelDoorLink.IsLockedDown = () => false;
            Assert.True(PanelDoorLink.Open("games"));
            Assert.Equal(1, fellBack);
        }
        finally
        {
            PanelDoorLink.OpenLauncherDoor = hook;
            PanelDoorLink.Fallback = fallback;
            PanelDoorLink.IsLockedDown = locked;
        }
    }

    // ccp-bugs #1701: during Lockdown the Games pill asked "Open Games?" and then nothing opened.
    // Blocking stays (the launcher is no way around the veil); the pill stops asking and says why.
    [Fact]
    public void DuringLockdownTheGamesPillSaysWhyInsteadOfAsking()
    {
        var locked = PanelDoorLink.IsLockedDown;
        try
        {
            PanelDoorLink.IsLockedDown = () => true;
            Assert.True(PanelDoorLink.RefusedNow("play"));
            Assert.True(PanelDoorLink.RefusedNow("PLAY"));
            Assert.False(PanelDoorLink.RefusedNow("playeyes"));   // a panel page is never refused here
            Assert.False(PanelDoorLink.RefusedNow("justdrop"));   // other window pills keep asking
            Assert.False(PanelDoorLink.RefusedNow(null));

            PanelDoorLink.IsLockedDown = () => throw new InvalidOperationException();
            Assert.True(PanelDoorLink.RefusedNow("play"));       // in doubt, locked

            PanelDoorLink.IsLockedDown = () => false;
            Assert.False(PanelDoorLink.RefusedNow("play"));
        }
        finally { PanelDoorLink.IsLockedDown = locked; }

        // The strip skips its "Open it?" card for a refused door, and ShowTab says why every time
        // in the launcher veil's existing words (no new loc key).
        var strip = AppFile("Controls", "NavRail", "SectionTabStrip.xaml.cs");
        Assert.Contains("NavStripRules.AsksBeforeOpening(tab) && !PanelDoorLink.RefusedNow(tab.Key)", strip, StringComparison.Ordinal);
        var nav = AppFile("MainWindow", "MainWindow.TabNavigation.cs");
        Assert.Contains("if (!Services.UI.PanelDoorLink.Open(launcherDoor))", nav, StringComparison.Ordinal);
        Assert.Contains("new Action(ShowDoorLockedNote)", nav, StringComparison.Ordinal);
        Assert.Equal("launcher_lockdown_veil", PanelDoorLink.LockdownLineKey);
    }

    [Fact]
    public void PlayIsEyesSessionsDeeperAndAGamesDoorAndLandsOnEyes()
    {
        var play = NavSections.Find(NavSections.Play)!;
        Assert.Equal("playeyes", play.DefaultTab);
        Assert.Equal(new[] { "playeyes", "playsessions", "deeper", "play" },
            play.Tabs.Where(t => !t.Hidden).Select(t => t.Key).ToArray());
    }

    // ccp-bugs #1633: 7.3.0 testers could not find the games from the panel. The Games pill is
    // back on Play as a WINDOW pill: visible, asks first, opens the launcher's Games door, and
    // never becomes a page (no lit pill, no slide slot, never remembered by the rail).
    [Fact]
    public void TheGamesPillIsAVisibleWindowPillThatOpensTheGamesDoor()
    {
        var games = NavStripRules.Pills(NavSections.Play).Single(t => t.Key == "play");
        Assert.Equal(NavTabKind.Window, games.Kind);
        Assert.Equal("nav_tab_games", games.LabelKey);
        Assert.Equal(0, games.Tier);
        Assert.False(games.Hidden);
        Assert.True(NavStripRules.AsksBeforeOpening(games));
        Assert.Equal(PanelDoorLink.Games, NavSections.LauncherDoorFor(games.Key));
        Assert.NotNull(NavStripRules.Glyph(games.Key));

        Assert.DoesNotContain("play", ConditioningControlPanel.Services.ChromeFxNav.NavOrder);
        Assert.Equal("playeyes", NavRailRules.TargetTab(NavSections.Play, "{\"play\":\"play\"}"));
        Assert.Equal("nav_tab_eyes", NavStripRules.PageLabelKey("lab"));

        // Its click goes through ShowTab, whose launcher-door intercept runs before any page code.
        var chrome = AppFile("MainWindow", "MainWindow.SectionChrome.cs");
        Assert.Contains("try { ShowTab(tab.Key); }", chrome, StringComparison.Ordinal);
    }

    [Fact]
    public void TheOldGamesKeyOpensTheGamesDoorAndIsNeverRemembered()
    {
        Assert.Equal("games", NavSections.LauncherDoorFor("play"));
        Assert.Equal("games", NavSections.LauncherDoorFor("PLAY"));
        Assert.Null(NavSections.LauncherDoorFor("playeyes"));
        Assert.Null(NavSections.LauncherDoorFor("lab"));
        Assert.Null(NavSections.LauncherDoorFor(null));
        foreach (var door in NavSections.LauncherDoors.Values)
            Assert.Contains(door, new[] { PanelDoorLink.Games, PanelDoorLink.Tools });

        // The chrome still files both old keys under Play, and a remembered "play" (every
        // existing profile has one) falls back to the section's landing instead of the launcher.
        Assert.Equal(NavSections.Play, NavSections.SectionForTab("play"));
        Assert.Equal(NavSections.Play, NavSections.SectionForTab("lab"));
        Assert.Equal("playeyes", NavStripRules.LastTabFor("{\"play\":\"play\"}", NavSections.Play));
        Assert.Equal("playeyes", NavRailRules.TargetTab(NavSections.Play, "{\"play\":\"play\"}"));
        Assert.Equal("playsessions", NavRailRules.TargetTab(NavSections.Play, "{\"play\":\"playsessions\"}"));
    }

    [Fact]
    public void TheSeamNamesNoLauncherTypeAndItsFallbackIsTheBackToCcLabsButton()
    {
        // The launcher lane owns Windows/Launcher and Services/Launcher; the seam must compile
        // without them, and unwired it presses the title-bar button rather than re-implementing it.
        // The seam is shared (CCP.Core) on the port; the WPF head wires its Lockdown check and
        // the fallback, which presses the title-bar button.
        var seam = AppFile("..", "CCP.Core", "Services", "UI", "PanelDoorLink.cs");
        Assert.DoesNotContain("LauncherHost", seam, StringComparison.Ordinal);
        Assert.DoesNotContain("Services.Launcher", seam, StringComparison.Ordinal);
        Assert.DoesNotContain("Windows.Launcher", seam, StringComparison.Ordinal);
        var head = AppFile("MainWindow", "MainWindow.xaml.cs");
        Assert.Contains("PanelDoorLink.IsLockedDown = () => App.Lockdown?.IsActive == true;", head, StringComparison.Ordinal);
        var fallback = head.Substring(head.IndexOf("PanelDoorLink.Fallback =", StringComparison.Ordinal));
        Assert.Contains("BtnBackToLauncher", fallback.Substring(0, 400), StringComparison.Ordinal);
    }

    [Fact]
    public void ArrivingOnPlaySaysWhereTheGamesWent()
    {
        var nav = AppFile("MainWindow", "MainWindow.TabNavigation.cs");
        Assert.Contains("new Action(ShowGamesMovedNote)", nav, StringComparison.Ordinal);
        // Existing keys only: no new loc key, so all ten language files already carry it.
        Assert.Contains("Loc.Get(\"launcher_window_title\"), Loc.Get(\"nav_tab_games\")", nav, StringComparison.Ordinal);
        Assert.Contains("NavMovedToastHits >= NavMovedNoteLimit", nav, StringComparison.Ordinal);
    }

    [Fact]
    public void JustDropLeftTheStripButKeepsItsOwner()
    {
        var studio = NavSections.Find(NavSections.Studio)!;
        Assert.DoesNotContain(studio.Tabs.Where(t => !t.Hidden), t => t.Key == "justdrop");
        Assert.Equal(NavSections.Studio, NavSections.SectionForTab("justdrop"));
        // ShowTab still launches the shop for the key.
        Assert.Contains("if (tab == \"justdrop\")", AppFile("MainWindow", "MainWindow.TabNavigation.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void ShowTabAsksTheSeamBeforeAnythingElseAndNobodyElseReachesAround()
    {
        var nav = AppFile("MainWindow", "MainWindow.TabNavigation.cs");
        var seam = nav.IndexOf("PanelDoorLink.Open(launcherDoor)", StringComparison.Ordinal);
        Assert.True(seam > 0, "ShowTab never opens a launcher door");
        Assert.True(seam < nav.IndexOf("NoteDestinationOpened(tab);", StringComparison.Ordinal),
            "the door check runs after the recents note");
        Assert.Contains("if (tab == \"lab\") tab = \"playeyes\";", nav, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGamesZoneAndTheDeeperTileAreCollapsedNotDeleted()
    {
        var play = AppFile("Views", "Tabs", "PlayTabView.xaml");
        Assert.Contains("x:Name=\"ZoneGamesHead\" x:FieldModifier=\"internal\" Margin=\"2,4,2,8\" Visibility=\"Collapsed\"", play, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ZoneGamesWall\" x:FieldModifier=\"internal\" Columns=\"3\" Visibility=\"Collapsed\"", play, StringComparison.Ordinal);

        var home = AppFile("Views", "Tabs", "SettingsTabView.xaml");
        Assert.Contains("x:Name=\"CardDeeperEditor\" x:FieldModifier=\"internal\" Visibility=\"Collapsed\"", home, StringComparison.Ordinal);
        // The daily box grew into the freed cell: no hole on the wall.
        Assert.Contains("Grid.Row=\"1\" Grid.RowSpan=\"2\" Grid.Column=\"0\" x:Name=\"MysteryFlipHost\"", home, StringComparison.Ordinal);
    }
}
