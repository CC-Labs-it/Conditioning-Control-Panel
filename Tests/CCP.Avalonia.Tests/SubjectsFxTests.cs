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
        TaskCompletionSource<PbpLobbyReply>? discovery = null;
        try
        {
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            var view = new AvailableSubjectsTabView();
            discovery = HoldDiscovery(view);
            var list = view.FindControl<ItemsControl>("AvailableSubjectsList")!;
            Paint(view, 9);
            window = new Window { Width = 1300, Height = 700, Content = view };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            view.IsVisible = false;
            view.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
            var cards = Enumerable.Range(0, 9).Select(i => list.ContainerFromIndex(i)!).ToArray();
            Assert.True(view.Discovery.IsWatched);
            Assert.False(discovery.Task.IsCompleted);
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
            discovery?.TrySetResult(PbpLobbyReply.Empty);
            Dispatcher.UIThread.RunJobs();
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
        TaskCompletionSource<PbpLobbyReply>? discovery = null;
        try
        {
            CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
            var view = new AvailableSubjectsTabView();
            discovery = HoldDiscovery(view);
            Paint(view, 3);
            window = new Window { Width = 1300, Height = 700, Content = view };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var list = view.FindControl<ItemsControl>("AvailableSubjectsList")!;
            Assert.True(view.Discovery.IsWatched);
            Assert.False(discovery.Task.IsCompleted);
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
            discovery?.TrySetResult(PbpLobbyReply.Empty);
            Dispatcher.UIThread.RunJobs();
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
        TaskCompletionSource<PbpLobbyReply>? discovery = null;
        try
        {
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            shell = new MainShellWindow();
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(MotionLevel.Full, CoreSettings.Current.MotionLevel);
            var view = shell.Named<AvailableSubjectsTabView>("AvailableSubjectsTab")!;
            discovery = HoldDiscovery(view);
            var list = view.FindControl<ItemsControl>("AvailableSubjectsList")!;
            Paint(view, 3); // the Lobby keeps its snapshot across the repaint on arrival
            shell.OpenNavSection("social");
            Dispatcher.UIThread.RunJobs();
            Assert.True(view.IsEffectivelyVisible);
            Assert.True(view.Discovery.IsWatched);
            Assert.False(discovery.Task.IsCompleted);
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
            discovery?.TrySetResult(PbpLobbyReply.Empty);
            Dispatcher.UIThread.RunJobs();
            CoreSettings.Current.MotionLevel = motion;
            SetClock(TimeProvider.System);
        }
        return Task.CompletedTask;
    });

    // These clock-stepped FX tests own the painted snapshot. Keep discovery in flight until
    // close so its signed-out/empty reply cannot replace the rows under animation. The page
    // still takes a real polling lease; LobbyPageTests covers completed discovery rounds.
    private static TaskCompletionSource<PbpLobbyReply> HoldDiscovery(AvailableSubjectsTabView view)
    {
        var reply = new TaskCompletionSource<PbpLobbyReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        view.ReadGates = () => LobbyGates.From(true, false, false);
        view.Discovery.SignedIn = () => true;
        view.Discovery.FetchChess = _ => reply.Task;
        view.Discovery.FetchGoon = _ => Task.FromResult(ConditioningControlPanel.Services.GoonGame.OpenTablesReply.Empty);
        view.Discovery.FetchRace = _ => Task.FromResult(RaceOpenReply.Empty);
        view.Discovery.ReadFriends = () => Array.Empty<ConditioningControlPanel.Services.Friends.Friend>();
        return reply;
    }

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
