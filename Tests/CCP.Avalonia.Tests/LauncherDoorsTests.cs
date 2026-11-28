using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Launcher;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The launcher's doors (declutter wave 2, twin of WPF feat/declutter-doors): the right side
/// opens on GAMES and TOOLS and no tiles; a door opens its page; Back returns.</summary>
public sealed class LauncherDoorsTests
{
    private static void Run(Action<MainShellWindow> body, MotionLevel motion = MotionLevel.Off)
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            CoreSettings.Current.Welcomed = true;
            CoreSettings.Current.HasAcceptedAgeVerification = true;
            CoreSettings.Current.MotionLevel = motion;
            CoreSettings.Current.LauncherLastPlayedId = "";
            CoreSettings.Current.LauncherSeenNewIds = new List<string>();
            var (oldIn, oldLab) = (CoreAccount.IsLoggedInProvider, CoreEntitlement.HasLabProvider);
            CoreAccount.IsLoggedInProvider = () => true;
            CoreEntitlement.HasLabProvider = () => true;
            var shell = new MainShellWindow();
            try
            {
                shell.Show();
                Dispatcher.UIThread.RunJobs();
                body(shell);
            }
            finally
            {
                LauncherWindow.Instance?.Close();
                LauncherWindow.Boot = BootDecision.PanelFirst;
                ConditioningControlPanel.Avalonia.Platform.StartupLadder.ResetForTests();
                LockdownService.Current = null;
                (CoreAccount.IsLoggedInProvider, CoreEntitlement.HasLabProvider) = (oldIn, oldLab);
                shell.Close();
                CoreSettings.ServiceProvider = null;
            }
        });
    }

    private static LauncherWindow OpenLauncher(MainShellWindow shell)
    {
        Assert.True(LauncherWindow.BackToLauncher(shell));
        shell.StepLauncherFade(1);
        Dispatcher.UIThread.RunJobs();
        return LauncherWindow.Instance!;
    }

    private static string[] Tiles(LauncherWindow w) =>
        w.FindControl<UniformGrid>("GamesGrid")!.Children.Select(c => (string)c.Tag!).ToArray();

    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, b));

    [Fact]
    public void Opens_on_two_doors_and_no_tiles() => Run(shell =>
    {
        var w = OpenLauncher(shell);
        Assert.Null(w.OpenDoor);
        Assert.Empty(Tiles(w));
        Assert.True(w.FindControl<UniformGrid>("DoorsHost")!.IsVisible);
        Assert.False(w.FindControl<ScrollViewer>("GamesScroller")!.IsVisible);
        Assert.False(w.FindControl<StackPanel>("DoorPageHeader")!.IsVisible);
        Assert.Equal(new object[] { LauncherDoor.Games, LauncherDoor.Tools }, w.DoorButtons.Select(b => b.Tag!).ToArray());
    });

    [Fact]
    public void A_door_page_lists_that_doors_cards_and_Back_returns() => Run(shell =>
    {
        var w = OpenLauncher(shell);
        Click(w.DoorButtons[0]);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(LauncherDoor.Games, w.OpenDoor);
        // One Breakout card (the demo on this head), then what this head can launch.
        Assert.Equal(new[] { "breakout", "piecebypiece", "race", "raceclassic", "dtrh", "intake" }, Tiles(w));
        Assert.True(w.FindControl<StackPanel>("DoorPageHeader")!.IsVisible);
        Assert.Equal(Loc.Get("launcher_section_games"), w.FindControl<TextBlock>("GamesEyebrow")!.Text);
        Assert.Empty(w.DoorButtons);

        Click(w.FindControl<Button>("DoorBack")!);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(w.OpenDoor);
        Assert.Empty(Tiles(w));
        Assert.Equal(2, w.DoorButtons.Count);

        Click(w.DoorButtons[1]);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(LauncherDoor.Tools, w.OpenDoor);
        var tools = Tiles(w);
        Assert.Equal(new[] { "deepereditor", "sessioneditor", "modcreator", "loom" },
            tools.Where(id => id != "justdrop").ToArray());
        Assert.Equal(Loc.Get("launcher_door_tools"), w.FindControl<TextBlock>("GamesEyebrow")!.Text);
        // Tools get no desktop shortcut.
        foreach (var tile in w.FindControl<UniformGrid>("GamesGrid")!.Children)
            Assert.DoesNotContain(tile.GetVisualDescendants().OfType<Button>(), b => Equals(b.Tag, "tile-shortcut"));
    });

    [Fact]
    public void Every_game_this_head_launches_stays_reachable() => Run(shell =>
    {
        var w = OpenLauncher(shell);
        w.SeatDoorPage(LauncherDoor.Games);
        var page = Tiles(w).ToHashSet();
        // Every game this head hosts has a card on the page (the demo through the one Breakout card).
        foreach (var card in LauncherWindow.VisibleCards)
            Assert.Contains(LauncherDoorRules.ListedId(card.Id), page);
        Assert.NotNull(LauncherCards.Find("breakoutdemo"));
        // The one card: the full game for an entitled account, the demo otherwise.
        Assert.Equal("breakout", LauncherWindow.LaunchIdFor("breakout"));
        CoreAccount.IsLoggedInProvider = () => false;
        Assert.Equal("breakoutdemo", LauncherWindow.LaunchIdFor("breakout"));
    });

    [Fact]
    public void Doors_are_named_for_UI_automation_and_focus_follows_the_keyboard() => Run(shell =>
    {
        var w = OpenLauncher(shell);
        var games = w.DoorButtons[0];
        Assert.Equal(Loc.Get("launcher_section_games") + ", " + string.Format(Loc.Get("launcher_door_count_games"), 6),
            AutomationProperties.GetName(games));
        Assert.Equal("LauncherDoor_games", AutomationProperties.GetAutomationId(games));
        Assert.Equal("LauncherDoor_tools", AutomationProperties.GetAutomationId(w.DoorButtons[1]));

        games.Focus(NavigationMethod.Tab);
        Click(games);
        Dispatcher.UIThread.RunJobs();
        var back = w.FindControl<Button>("DoorBack")!;
        Assert.True(back.IsFocused);
        Assert.Equal(Loc.Get("btn_back"), AutomationProperties.GetName(back));

        Click(back);
        Dispatcher.UIThread.RunJobs();
        Assert.True(w.DoorButtons[0].IsFocused);
    });

    [Fact]
    public void Continue_shows_for_the_last_played_game_and_hides_when_locked_or_unknown() => Run(shell =>
    {
        var w = OpenLauncher(shell);
        Assert.Null(w.ContinueButton);

        CoreSettings.Current.LauncherLastPlayedId = "nope";
        w.SeatDoorPage(null);
        Assert.Null(w.ContinueButton);

        CoreSettings.Current.LauncherLastPlayedId = "loom";          // a tool is never continued
        w.SeatDoorPage(null);
        Assert.Null(w.ContinueButton);

        CoreSettings.Current.LauncherLastPlayedId = "breakoutdemo";  // the folded card answers
        w.SeatDoorPage(null);
        Assert.NotNull(w.ContinueButton);
        Assert.Equal(Loc.GetF("launcher_door_continue", Loc.Get("launcher_game_breakout_title")),
            AutomationProperties.GetName(w.ContinueButton!));

        CoreSettings.Current.LauncherLastPlayedId = "intake";
        CoreEntitlement.HasLabProvider = () => false;
        var oldPass = CoreEntitlement.IntakePassAvailableProvider;
        try
        {
            CoreEntitlement.IntakePassAvailableProvider = () => false;   // Graded Intake is locked
            w.SeatDoorPage(null);
            Assert.Null(w.ContinueButton);
        }
        finally { CoreEntitlement.IntakePassAvailableProvider = oldPass; }
    });

    [Fact]
    public void Continue_label_fits_the_pill_and_constrains_long_words()
    {
        var oldProvider = CoreSettings.ServiceProvider;
        try
        {
            Run(shell =>
            {
                var w = OpenLauncher(shell);
                CoreSettings.Current.LauncherLastPlayedId = "breakoutdemo";
                w.SeatDoorPage(null);
                var button = Assert.IsType<Button>(w.ContinueButton);
                Assert.Equal(560, button.MaxWidth);
                var label = Assert.IsType<DockPanel>(button.Content);
                Assert.Equal(new Thickness(18, 0, 20, 0), label.Margin);
                var words = Assert.IsType<TextBlock>(label.Children[1]);
                words.Text = "Continue: Terms & Conditioning with a very long title";
                label.Measure(new Size(200, 46));
                label.Arrange(new Rect(0, 0, 200, 46));
                Assert.True(words.Bounds.Right <= label.Bounds.Width, $"Words end at {words.Bounds.Right}, width {label.Bounds.Width}");
                Assert.Equal(global::Avalonia.Media.TextTrimming.CharacterEllipsis, words.TextTrimming);
            });
        }
        finally { CoreSettings.ServiceProvider = oldProvider; }
    }

    [Fact]
    public void A_launch_from_a_page_is_remembered_and_Continue_opens_it() => Run(shell =>
    {
        var w = OpenLauncher(shell);
        w.SeatDoorPage(LauncherDoor.Games);
        Assert.True(w.Play(LauncherCards.Find("intake")!));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("intake", CoreSettings.Current.LauncherLastPlayedId);
        Assert.Equal("gradedintake", shell.CurrentTab);
        // A tool is not remembered.
        LauncherWindow.BackToLauncher(shell);
        shell.StepLauncherFade(1);
        Dispatcher.UIThread.RunJobs();
        Assert.True(w.Play(LauncherCards.FindAny("sessioneditor")!));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("intake", CoreSettings.Current.LauncherLastPlayedId);
    });

    [Fact]
    public void New_stamp_shows_until_the_games_page_is_opened() => Run(shell =>
    {
        var w = OpenLauncher(shell);
        Assert.NotNull(w.NewStamp);   // the Breakout and Piece by Piece cards are NEW
        w.SeatDoorPage(LauncherDoor.Games);
        Assert.Contains("breakout", CoreSettings.Current.LauncherSeenNewIds);
        w.SeatDoorPage(null);
        Assert.Null(w.NewStamp);
    });

    [Fact]
    public void ShowDoor_opens_on_the_page_and_Lockdown_refuses_it() => Run(shell =>
    {
        Assert.True(LauncherWindow.ShowDoor(shell, LauncherDoor.Tools));
        shell.StepLauncherFade(1);
        Dispatcher.UIThread.RunJobs();
        var w = LauncherWindow.Instance!;
        Assert.True(w.IsVisible);
        Assert.Equal(LauncherDoor.Tools, w.OpenDoor);
        Assert.Contains("loom", Tiles(w));

        // Already up: the page swaps in place.
        Assert.True(LauncherWindow.ShowDoor(shell, LauncherDoor.Games));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(LauncherDoor.Games, w.OpenDoor);

        w.OpenPanel();
        Dispatcher.UIThread.RunJobs();
        Assert.True(shell.IsVisible);
        var lockdown = LockdownService.Current = new LockdownService();
        try
        {
            lockdown.Activate(TimeSpan.FromMinutes(30));
            Assert.True(MainShellWindow.LockdownActive);
            Assert.False(LauncherWindow.ShowDoor(shell, LauncherDoor.Games));
            Assert.True(shell.IsVisible);
        }
        finally { lockdown.Deactivate(); }
    });

    [Fact]
    public void With_motion_on_the_open_beat_lands_on_the_clock_and_the_doors_rest_after() => Run(shell =>
    {
        var w = OpenLauncher(shell);
        Click(w.DoorButtons[0]);
        for (int i = 0; i < 30 && w.OpenDoor == null; i++) w.StepFx(0.05);
        Assert.Equal(LauncherDoor.Games, w.OpenDoor);
        Assert.NotEmpty(Tiles(w));
        Click(w.FindControl<Button>("DoorBack")!);
        for (int i = 0; i < 30 && w.OpenDoor != null; i++) w.StepFx(0.05);
        Assert.Null(w.OpenDoor);
        for (int i = 0; i < 40; i++) w.StepFx(0.05);
        Assert.All(w.DoorButtons, b => Assert.Equal(1, ((Control)b.Parent!).Opacity, 3));
    }, MotionLevel.Full);
}
