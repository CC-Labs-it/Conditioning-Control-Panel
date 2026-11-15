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
        try
        {
            var opened = new List<string>();
            var fellBack = 0;
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
        }
    }

    [Fact]
    public void PlayIsEyesSessionsAndDeeperAndLandsOnEyes()
    {
        var play = NavSections.Find(NavSections.Play)!;
        Assert.Equal("playeyes", play.DefaultTab);
        Assert.Equal(new[] { "playeyes", "playsessions", "deeper" },
            play.Tabs.Where(t => !t.Hidden).Select(t => t.Key).ToArray());
        Assert.DoesNotContain(NavSections.AllTabs, t => t.Key == "play");
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
        var seam = AppFile("Services", "UI", "PanelDoorLink.cs");
        Assert.DoesNotContain("LauncherHost", seam, StringComparison.Ordinal);
        Assert.DoesNotContain("Services.Launcher", seam, StringComparison.Ordinal);
        Assert.DoesNotContain("Windows.Launcher", seam, StringComparison.Ordinal);
        Assert.Contains("BtnBackToLauncher", seam, StringComparison.Ordinal);
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
