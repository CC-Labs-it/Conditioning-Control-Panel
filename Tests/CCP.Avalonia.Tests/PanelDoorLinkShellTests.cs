using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Declutter wave 2 (2026-11-15), the Avalonia twin of WPF feat/declutter-stripout: the games
/// left the Play page for the launcher's Games door, Just Drop left the Studio strip, the Home
/// Deeper Editor tile left the wall. Every old key still lands somewhere, through ONE seam
/// (Core PanelDoorLink) that opens nothing during Lockdown.
/// </summary>
public sealed class PanelDoorLinkShellTests
{
    private static void SetUp()
    {
        if (Application.Current != null) return;
        AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
            .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
    }

    /// <summary>A shown shell on fresh settings, the seam's statics restored afterwards.</summary>
    private static Task Shell(Action<MainShellWindow> body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        SetUp();
        var previousProvider = global::ConditioningControlPanel.CoreSettings.ServiceProvider;
        var service = new global::ConditioningControlPanel.Services.SettingsService();
        global::ConditioningControlPanel.CoreSettings.ServiceProvider = () => service;
        var st = global::ConditioningControlPanel.CoreSettings.Current;
        (st.Welcomed, st.HasAcceptedAgeVerification, st.WhatMovedCardShown) = (true, true, 1);   // no intro takes focus
        var hook = PanelDoorLink.OpenLauncherDoor;
        var fallback = PanelDoorLink.Fallback;
        var locked = PanelDoorLink.IsLockedDown;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            body(shell);
        }
        finally
        {
            shell.Close();
            PanelDoorLink.OpenLauncherDoor = hook;
            PanelDoorLink.Fallback = fallback;
            PanelDoorLink.IsLockedDown = locked;
            global::ConditioningControlPanel.CoreSettings.ServiceProvider = previousProvider;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task TheShellWiresTheLockdownCheckAndTheBackToCcLabsFallback() => Shell(shell =>
    {
        Assert.NotNull(PanelDoorLink.Fallback);
        Assert.False(PanelDoorLink.IsLockedDown());
        Assert.NotNull(shell.Named<Button>("BtnBackToLauncher"));
    });

    [Fact]
    public Task PlayOpensTheGamesDoorAndLeavesThePageOnScreen() => Shell(shell =>
    {
        var opened = new List<string>();
        PanelDoorLink.OpenLauncherDoor = opened.Add;
        shell.ShowTab("presets");
        Dispatcher.UIThread.RunJobs();

        shell.ShowTab("play");
        shell.ShowTab("PLAY");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { "games", "games" }, opened);
        Assert.Equal("presets", shell.CurrentTab);
        Assert.False(shell.PageStrip!.MovedNoteShown);
    });

    [Fact]
    public Task DuringLockdownPlayOpensNothingAndTheStripSaysWhyEveryTime() => Shell(shell =>
    {
        var opened = new List<string>();
        var fellBack = 0;
        PanelDoorLink.OpenLauncherDoor = opened.Add;
        PanelDoorLink.Fallback = () => fellBack++;
        PanelDoorLink.IsLockedDown = () => true;
        shell.ShowTab("presets");
        Dispatcher.UIThread.RunJobs();

        global::ConditioningControlPanel.CoreSettings.Current.NavMovedToastHits = 3;
        for (var i = 0; i < 4; i++)
        {
            shell.ShowTab("play");
            Assert.True(shell.PageStrip!.MovedNoteShown);
            Assert.Equal(global::ConditioningControlPanel.Localization.Loc.Get(PanelDoorLink.LockdownLineKey),
                shell.PageStrip.MovedNoteTextForTests);
            if (i < 3) shell.PageStrip.HideNote();
        }
        Assert.Equal(3, global::ConditioningControlPanel.CoreSettings.Current.NavMovedToastHits);
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(opened);
        Assert.Equal(0, fellBack);
        Assert.Equal("presets", shell.CurrentTab);
        Assert.True(shell.PageStrip!.MovedNoteShown);
        Assert.Contains(global::ConditioningControlPanel.Localization.Loc.Get(PanelDoorLink.LockdownLineKey),
            shell.PageStrip.MovedNoteTextForTests ?? "", StringComparison.Ordinal);
    });

    [Fact]
    public Task PanicStopsTheLockdownNoteAndItsTimer() => Shell(shell =>
    {
        PanelDoorLink.IsLockedDown = () => true;
        shell.ShowTab("play");
        var strip = shell.PageStrip!;
        Assert.True(strip.MovedNoteShown);
        PanicSurfaces.All.Single(s => s.Id == "nav-window-confirm").Stop(shell);
        Assert.False(strip.MovedNoteShown);
        var timer = (DispatcherTimer?)typeof(ConditioningControlPanel.Avalonia.Views.Controls.SectionTabStrip)
            .GetField("_noteTimer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(strip);
        Assert.NotNull(timer);
        Assert.False(timer!.IsEnabled);
    });

    [Fact]
    public Task LabLandsOnPlayEyesAndThePlayStripHasNoGamesPill() => Shell(shell =>
    {
        PanelDoorLink.OpenLauncherDoor = _ => throw new Xunit.Sdk.XunitException("lab must not open the launcher");
        shell.ShowTab("lab");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("playeyes", shell.CurrentTab);
        var strip = shell.PageStrip!;
        Assert.Equal(new[] { "playeyes", "playsessions", "deeper" }, strip.PillKeys);
        Assert.Equal("playeyes", strip.ActivePillKey);
    });

    [Fact]
    public Task ThePlayRailDoorShowsThePageFromTheTopAndNamesTheMoveForGamesUsers() => Shell(shell =>
    {
        var st = global::ConditioningControlPanel.CoreSettings.Current;
        st.NavLastTabBySection = "{\"play\":\"play\"}";   // remembered: the old Games pill
        PanelDoorLink.OpenLauncherDoor = _ => throw new Xunit.Sdk.XunitException("the rail door must not open the launcher");
        shell.ShowTab("presets");
        Dispatcher.UIThread.RunJobs();

        shell.OpenNavSection(NavSections.Play);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("playeyes", shell.CurrentTab);
        var scroll = shell.Named<ConditioningControlPanel.Avalonia.Views.Tabs.PlayTabView>("PlayTab")!.FindControl<ScrollViewer>("WallScroll")!;
        Assert.Equal(0, scroll.Offset.Y);
        Assert.True(shell.PageStrip!.MovedNoteShown);
        Assert.Equal(1, st.NavMovedToastHits);
    });

    [Fact]
    public Task TheGamesZoneIsCollapsedWithItsNamesKept() => Shell(shell =>
    {
        shell.ShowTab("playeyes");
        Dispatcher.UIThread.RunJobs();
        var play = shell.Named<ConditioningControlPanel.Avalonia.Views.Tabs.PlayTabView>("PlayTab")!;
        Assert.False(play.FindControl<Control>("ZoneGames")!.IsVisible);
        Assert.False(play.FindControl<Control>("GamesGrid")!.IsVisible);
        foreach (var name in new[] { "SlotBreakoutDemo", "BtnPlayBreakoutDemo" })
        {
            var c = play.FindControl<Control>(name);
            Assert.True(c != null, name + " keeps its x:Name");
            Assert.False(c!.IsEffectivelyVisible, name + " sits in the collapsed games zone");
        }
        Assert.True(play.FindControl<Control>("ZoneEyes")!.IsEffectivelyVisible);
        Assert.True(play.FindControl<Control>("ZoneSessions")!.IsEffectivelyVisible);
    });

    [Fact]
    public Task HomeDropsTheDeeperEditorTileAndTheGiftPlateSpansItsSlot() => Shell(shell =>
    {
        shell.ShowTab("settings");
        Dispatcher.UIThread.RunJobs();
        var dash = shell.SettingsPage!;
        var tile = dash.FindControl<Control>("CardDeeperEditor");
        Assert.NotNull(tile);
        Assert.False(tile!.IsVisible);
        var host = dash.FindControl<Control>("MysteryFlipHost")!;
        Assert.Equal(1, Grid.GetRow(host));
        Assert.Equal(2, Grid.GetRowSpan(host));
        Assert.Equal(0, Grid.GetColumn(host));
        Assert.Equal(1, Grid.GetRow(dash.FindControl<Control>("MysteryBadge")!));
        var face = dash.FindControl<Border>("MysteryRevealFace")!;
        var art = face.GetVisualDescendants().OfType<Border>().Select(b => b.Background).OfType<ImageBrush>().First();
        Assert.Equal(Stretch.Uniform, art.Stretch);
    });
}
