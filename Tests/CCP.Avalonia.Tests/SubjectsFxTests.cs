using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Skia;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Lobby;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class SubjectsFxTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public Task CardsStaggerOnlyOnArrivalWithoutTouchingTheRowCard() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var previous = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var clock = new Clock();
        SetClock(clock);
        Window? window = null;
        try
        {
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            var view = new AvailableSubjectsTabView();
            var list = view.FindControl<ItemsControl>("AvailableSubjectsList")!;
            Paint(view, 9);
            window = new Window { Width = 1300, Height = 700, Content = view };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            view.IsVisible = false;
            view.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
            var cards = Enumerable.Range(0, 9).Select(i => list.ContainerFromIndex(i)!).ToArray();
            Assert.Equal(0, cards[0].Opacity);
            Assert.Equal(10, Assert.IsType<TranslateTransform>(cards[0].RenderTransform).Y);
            Step(view, clock, 100);
            Assert.InRange(cards[0].Opacity, 0.1, 0.99);
            Assert.True(cards[0].Opacity > cards[2].Opacity);
            Step(view, clock, 160);
            Assert.True(cards[5].Opacity > cards[6].Opacity);
            Assert.Equal(cards[6].Opacity, cards[8].Opacity);
            Step(view, clock, 300);
            Assert.All(cards, c => Assert.Equal(1, c.Opacity));
            Assert.False(view.SubjectsFxRunning);
            // The container animates, never the bound row card inside it.
            Assert.All(cards, c => Assert.Equal(1, c.GetVisualDescendants().OfType<Border>().First().Opacity));

            // A service rebind while the page is being read must never replay the entrance.
            Paint(view, 1);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, list.ContainerFromIndex(0)!.Opacity);
            view.IsVisible = false; view.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, list.ContainerFromIndex(0)!.Opacity);
            view.IsVisible = false;
            Assert.Equal(1, list.ContainerFromIndex(0)!.Opacity);
            Assert.False(view.SubjectsFxRunning);
        }
        finally
        {
            window?.Close();
            SetClock(TimeProvider.System);
            service.SaveImmediate();
            CoreSettings.ServiceProvider = previous;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ReducedStillEntersAndOffNeverStartsAnEntrance() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var previous = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var clock = new Clock();
        SetClock(clock);
        Window? window = null;
        try
        {
            CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
            var view = new AvailableSubjectsTabView();
            Paint(view, 3);
            window = new Window { Width = 1300, Height = 700, Content = view };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var list = view.FindControl<ItemsControl>("AvailableSubjectsList")!;
            Assert.Equal(0, list.ContainerFromIndex(0)!.Opacity); // Reduced still permits interaction entrances
            Step(view, clock, 600);
            Assert.Equal(1, list.ContainerFromIndex(0)!.Opacity);

            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            view.IsVisible = false; view.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, list.ContainerFromIndex(0)!.Opacity);
            Assert.False(view.SubjectsFxRunning);
        }
        finally
        {
            window?.Close();
            SetClock(TimeProvider.System);
            service.SaveImmediate();
            CoreSettings.ServiceProvider = previous;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task SocialDoorStartsEntranceAndLeavingParksIt() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var motion = CoreSettings.Current.MotionLevel;
        var clock = new Clock();
        SetClock(clock);
        MainShellWindow? shell = null;
        try
        {
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            shell = new MainShellWindow();
            var view = shell.Named<AvailableSubjectsTabView>("AvailableSubjectsTab")!;
            Paint(view, 3);
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(MotionLevel.Full, CoreSettings.Current.MotionLevel);
            var list = view.FindControl<ItemsControl>("AvailableSubjectsList")!;
            Paint(view, 3); // the Lobby keeps its snapshot across the repaint on arrival
            shell.OpenNavSection("social");
            Dispatcher.UIThread.RunJobs();
            Assert.True(view.IsEffectivelyVisible);
            Assert.Equal(0, list.ContainerFromIndex(0)!.Opacity);
            Assert.True(view.SubjectsFxRunning);
            shell.ShowTab("settings");
            Dispatcher.UIThread.RunJobs();
            Assert.False(view.SubjectsFxRunning);
            Assert.Equal(1, list.ContainerFromIndex(0)!.Opacity);
        }
        finally
        {
            shell?.Close();
            CoreSettings.Current.MotionLevel = motion;
            SetClock(TimeProvider.System);
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task RaceRowsDrawTheirArtAndLookStaggerOnFirstFillAndANewOnePops() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        // WPF MainWindow.Lobby.cs:146-209 and LobbyRowView.cs:27-57.
        Setup();
        var previous = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var clock = new Clock();
        SetClock(clock);
        Window? window = null;
        try
        {
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            var view = new AvailableSubjectsTabView();
            Paint(view, 0);
            window = new Window { Width = 1300, Height = 700, Content = view };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var open = view.FindControl<ItemsControl>("AvailableSubjectsList")!;
            var friends = view.FindControl<ItemsControl>("LobbyFriendsList")!;
            var gates = LobbyGates.From(true, false, false);
            static LobbyRow Race(string key, PlayerLook? look = null) =>
                new() { Game = LobbyGame.Race, Key = key, HostName = "Host", Look = look ?? PlayerLook.None };
            var friend = Race("friendroom01", new PlayerLook("nobody", null)) with { Friend = true };
            view.PaintSnapshot(new LobbySnapshot(new[] { Race("aaaaaaaaaaaa") }, Array.Empty<LobbyRow>(), new[] { friend }, true), gates);
            Dispatcher.UIThread.RunJobs();
            // The first fill staggers every column in, not only Open.
            Assert.Equal(0, open.ContainerFromIndex(0)!.Opacity);
            Assert.Equal(0, friends.ContainerFromIndex(0)!.Opacity);
            var card = (LobbyCardView)open.Items[0]!;
            Assert.NotNull(Assert.IsType<ImageBrush>(card.ArtBrush).Source);
            Assert.False(card.HasLook);
            Assert.False(Tagged<Grid>(open.ContainerFromIndex(0)!, "lobby-face").IsVisible);
            var face = (LobbyCardView)friends.Items[0]!;
            Assert.True(face.HasLook);
            Assert.NotNull(face.FaceBrush);
            Assert.True(Tagged<Grid>(friends.ContainerFromIndex(0)!, "lobby-face").IsVisible);
            Step(view, clock, 600);
            Assert.Equal(1, friends.ContainerFromIndex(0)!.Opacity);

            // A later poll: only the new table pops and rings, and nothing replays the stagger.
            view.PaintSnapshot(new LobbySnapshot(new[] { Race("aaaaaaaaaaaa"), Race("bbbbbbbbbbbb") }, Array.Empty<LobbyRow>(), new[] { friend }, true), gates);
            Dispatcher.UIThread.RunJobs();
            var fresh = open.ContainerFromIndex(1)!;
            var pop = Assert.IsType<ScaleTransform>(Tagged<Grid>(fresh, "lobby-card").RenderTransform);
            var ring = Tagged<global::Avalonia.Controls.Shapes.Rectangle>(fresh, "lobby-ring");
            Assert.Equal(0.9, pop.ScaleX);
            Assert.Equal(0.9, ring.Opacity);
            Assert.Null(Tagged<Grid>(open.ContainerFromIndex(0)!, "lobby-card").RenderTransform);
            Assert.Equal(1, open.ContainerFromIndex(0)!.Opacity);
            Step(view, clock, 200);
            Assert.InRange(pop.ScaleX, 0.95, 1.05);
            Assert.InRange(ring.Opacity, 0.01, 0.89);
            Step(view, clock, 400);
            Assert.Equal(1, pop.ScaleX);
            Assert.Equal(0, ring.Opacity);
            Assert.False(view.SubjectsFxRunning);

            // Reduced keeps the pop and drops the ring; Off moves nothing.
            CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
            view.PaintSnapshot(new LobbySnapshot(new[] { Race("cccccccccccc") }, Array.Empty<LobbyRow>(), Array.Empty<LobbyRow>(), true), gates);
            Dispatcher.UIThread.RunJobs();
            var reduced = open.ContainerFromIndex(0)!;
            Assert.Equal(0.9, Assert.IsType<ScaleTransform>(Tagged<Grid>(reduced, "lobby-card").RenderTransform).ScaleX);
            Assert.Equal(0, Tagged<global::Avalonia.Controls.Shapes.Rectangle>(reduced, "lobby-ring").Opacity);
            Step(view, clock, 600);
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            view.PaintSnapshot(new LobbySnapshot(new[] { Race("dddddddddddd") }, Array.Empty<LobbyRow>(), Array.Empty<LobbyRow>(), true), gates);
            Dispatcher.UIThread.RunJobs();
            var still = Tagged<Grid>(open.ContainerFromIndex(0)!, "lobby-card").RenderTransform as ScaleTransform;
            Assert.True(still == null || still.ScaleX == 1);
            Assert.False(view.SubjectsFxRunning);
        }
        finally
        {
            window?.Close();
            SetClock(TimeProvider.System);
            service.SaveImmediate();
            CoreSettings.ServiceProvider = previous;
        }
        return Task.CompletedTask;
    });

    private static T Tagged<T>(Control root, string tag) where T : Control =>
        root.GetVisualDescendants().OfType<T>().First(c => Equals(c.Tag, tag));

    // WPF staggers AvailableSubjectsList, which is the Lobby's Open column (MainWindow.SubjectsFx.cs:94).
    private static void Paint(AvailableSubjectsTabView view, int open)
    {
        view.ReadGates = () => LobbyGates.From(true, false, false);
        // Keep the fixture roster while exercising real watch ownership; no network or completed refresh.
        view.CreateLobby = () => new LobbyPoll(new ConditioningControlPanel.Avalonia.Platform.FriendsHead.Timer(),
            () => true, () => Array.Empty<ConditioningControlPanel.Services.Friends.Friend>(),
            () => new TaskCompletionSource<PbpLobbyReply>().Task, () => throw new InvalidOperationException(),
            () => throw new InvalidOperationException());
        view.PaintSnapshot(
        new LobbySnapshot(Enumerable.Range(0, open).Select(i => new LobbyRow
            { Game = LobbyGame.Remote, Key = "r" + i, HostName = "Host" + i, RemoteTier = "light" }).ToArray(),
            Array.Empty<LobbyRow>(), Array.Empty<LobbyRow>(), true), LobbyGates.From(true, false, false));
    }

    private static void SetClock(TimeProvider clock) => AvailableSubjectsTabView.FxTime = clock;
    private static void Step(AvailableSubjectsTabView view, Clock clock, int ms)
    {
        clock.Now += TimeSpan.FromMilliseconds(ms).Ticks;
        view.StepSubjectsFx();
        Dispatcher.UIThread.RunJobs();
    }
    private sealed class Clock : TimeProvider
    {
        internal long Now = 1_000_000;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }
}
