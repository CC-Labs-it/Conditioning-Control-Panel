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
            Assert.All(cards, c => Assert.Equal(1, c.GetVisualDescendants().OfType<Border>().Single(b => Equals(b.Tag, "lobby-face")).Opacity));

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
    public Task FirstLiveFillStaggersAllLobbyColumns() => AvaloniaTestDispatcher.RunAsync(() =>
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
            window = new Window { Width = 1300, Height = 700, Content = view };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            view.PaintSnapshot(new LobbySnapshot(Array.Empty<LobbyRow>(), Array.Empty<LobbyRow>(), Array.Empty<LobbyRow>(), false),
                LobbyGates.From(false, false, false));
            Dispatcher.UIThread.RunJobs();
            var row = new LobbyRow { Game = LobbyGame.Remote, Key = "one", HostName = "Host" };
            view.PaintSnapshot(new LobbySnapshot(new[] { row }, new[] { row }, new[] { row }, true),
                LobbyGates.From(true, false, false));
            Dispatcher.UIThread.RunJobs();
            var lists = new[] { "AvailableSubjectsList", "LobbyPlayingList", "LobbyFriendsList" }
                .Select(name => view.FindControl<ItemsControl>(name)!).ToArray();
            Assert.All(lists, list => Assert.Equal(0, list.ContainerFromIndex(0)!.Opacity));
            Step(view, clock, 600);
            Assert.All(lists, list => Assert.Equal(1, list.ContainerFromIndex(0)!.Opacity));
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

    [Theory]
    [InlineData(MotionLevel.Full)]
    [InlineData(MotionLevel.Reduced)]
    [InlineData(MotionLevel.Off)]
    public Task NewRowsPopAcrossColumnsAndHideCleansUp(MotionLevel motion) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var previous = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var clock = new Clock(); SetClock(clock);
        Window? window = null;
        try
        {
            CoreSettings.Current.MotionLevel = motion;
            var view = new AvailableSubjectsTabView();
            window = new Window { Width = 1300, Height = 700, Content = view }; window.Show();
            Dispatcher.UIThread.RunJobs();
            LobbyRow Row(string key) => new() { Game = LobbyGame.Remote, Key = key, HostName = key };
            var old = Row("old");
            var gates = LobbyGates.From(true, false, false);
            view.PaintSnapshot(new LobbySnapshot(new[] { old }, Array.Empty<LobbyRow>(), Array.Empty<LobbyRow>(), true), gates);
            Dispatcher.UIThread.RunJobs(); Step(view, clock, 600);
            view.PaintSnapshot(new LobbySnapshot(new[] { old, Row("a") }, new[] { Row("b") }, new[] { Row("c") }, true), gates);
            Dispatcher.UIThread.RunJobs();
            var lists = new[] { "AvailableSubjectsList", "LobbyPlayingList", "LobbyFriendsList" }.Select(n => view.FindControl<ItemsControl>(n)!).ToArray();
            Control Card(int column, int index, string tag) => lists[column].ContainerFromIndex(index)!.GetVisualDescendants().OfType<Control>().First(c => Equals(c.Tag, tag));
            Assert.Null(Card(0, 0, "lobby-card").RenderTransform);
            foreach (var (column, index) in new[] { (0, 1), (1, 0), (2, 0) })
            {
                var card = Card(column, index, "lobby-card");
                var ring = Card(column, index, "lobby-ring");
                if (motion == MotionLevel.Off) { Assert.Null(card.RenderTransform); Assert.Equal(0, ring.Opacity); }
                else
                {
                    Assert.Equal(0.9, Assert.IsType<ScaleTransform>(card.RenderTransform).ScaleX, 3);
                    Assert.Equal(motion == MotionLevel.Full ? 0.9 : 0, ring.Opacity, 3);
                }
            }
            var grid = Assert.IsType<Grid>(Card(0, 1, "lobby-card"));
            var ringBorder = Assert.IsType<Border>(Card(0, 1, "lobby-ring"));
            Assert.Same(ringBorder, grid.Children[0]);
            Assert.Equal("lobby-face", Assert.IsType<Border>(grid.Children[1]).Tag);
            Assert.Equal(new CornerRadius(16), ringBorder.CornerRadius);
            Assert.Equal(Color.Parse("#FFFF8CC0"), Assert.IsAssignableFrom<ISolidColorBrush>(ringBorder.BorderBrush).Color);
            Assert.Equal(new RelativePoint(0.5, 0.5, RelativeUnit.Relative), grid.RenderTransformOrigin);
            Assert.Equal(new RelativePoint(0.5, 0.5, RelativeUnit.Relative), ringBorder.RenderTransformOrigin);

            if (motion != MotionLevel.Off)
            {
                var scale = Assert.IsType<ScaleTransform>(grid.RenderTransform);
                Step(view, clock, 80);
                Assert.Equal(0.984329, scale.ScaleX, 6);
                Step(view, clock, 80);
                Assert.Equal(1.0125, scale.ScaleX, 5);
                Assert.Equal(scale.ScaleX, scale.ScaleY, 5);
                if (motion == MotionLevel.Full)
                {
                    var ringScale = Assert.IsType<ScaleTransform>(ringBorder.RenderTransform);
                    Assert.Equal(1.0416568, ringScale.ScaleX, 6);
                    Assert.Equal(ringScale.ScaleX, ringScale.ScaleY, 6);
                    Assert.Equal(0.6230769, ringBorder.Opacity, 6);
                }
                else Assert.Equal(0, ringBorder.Opacity);
                Step(view, clock, 80);
                Assert.Equal(1.007276, scale.ScaleX, 6);
                Step(view, clock, 80);
                Assert.Equal(1, scale.ScaleX, 6);
                Assert.Equal(1, scale.ScaleY, 6);
                Assert.Equal(motion == MotionLevel.Full, view.SubjectsFxRunning);
                if (motion == MotionLevel.Full)
                {
                    Assert.Equal(1.0681657, Assert.IsType<ScaleTransform>(ringBorder.RenderTransform).ScaleX, 6);
                    Assert.Equal(0.3461538, ringBorder.Opacity, 6);
                }
                Step(view, clock, 200);
                Assert.False(view.SubjectsFxRunning);
                Assert.Equal(1, scale.ScaleX, 6);
                Assert.Equal(0, ringBorder.Opacity, 6);
                if (motion == MotionLevel.Full)
                    Assert.Equal(1.08, Assert.IsType<ScaleTransform>(ringBorder.RenderTransform).ScaleX, 6);

                // Start a fresh effect so hide cleanup is checked while the clock is running.
                view.PaintSnapshot(new LobbySnapshot(new[] { old, Row("d") }, Array.Empty<LobbyRow>(), Array.Empty<LobbyRow>(), true), gates);
                Dispatcher.UIThread.RunJobs();
                Assert.True(view.SubjectsFxRunning);
            }
            view.IsVisible = false;
            Assert.False(view.SubjectsFxRunning);
            if (motion != MotionLevel.Off)
            {
                Assert.Equal(1, Assert.IsType<ScaleTransform>(Card(0, 1, "lobby-card").RenderTransform).ScaleX);
                Assert.Equal(0, Card(0, 1, "lobby-ring").Opacity);
            }
        }
        finally { window?.Close(); SetClock(TimeProvider.System); service.SaveImmediate(); CoreSettings.ServiceProvider = previous; }
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
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(MotionLevel.Full, CoreSettings.Current.MotionLevel);
            var view = shell.Named<AvailableSubjectsTabView>("AvailableSubjectsTab")!;
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

    // WPF staggers AvailableSubjectsList, which is the Lobby's Open column (MainWindow.SubjectsFx.cs:94).
    private static void Paint(AvailableSubjectsTabView view, int open) => view.PaintSnapshot(
        new LobbySnapshot(Enumerable.Range(0, open).Select(i => new LobbyRow
            { Game = LobbyGame.Remote, Key = "r" + i, HostName = "Host" + i, RemoteTier = "light" }).ToArray(),
            Array.Empty<LobbyRow>(), Array.Empty<LobbyRow>(), true), LobbyGates.From(true, false, false));

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
