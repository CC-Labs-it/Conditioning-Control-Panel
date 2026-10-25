using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Skia;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Startup;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class StartupLadderTests
{
    private static Task Run(Action<Window, StartupLadder.TestClock> test) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        StartupLadder.ResetForTests();
        var owner = new Window();
        try { owner.Show(); test(owner, (StartupLadder.TestClock)StartupLadder.Time); }
        finally { owner.Close(); StartupLadder.ResetForTests(); Dispatcher.UIThread.RunJobs(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task QueueDefersPassiveUntilDrainAndKeepsPriorityFifoAndDedupe() => Run((owner, clock) =>
    {
        var calls = new List<string>();
        int drained = 0;
        StartupLadder.Drained += () => drained++;
        void Queue(string key, int priority) => StartupLadder.EnqueueModal(key, priority, owner,
            () => { calls.Add(key); return Task.CompletedTask; });
        // WPF pins the first selected key even if a higher priority arrives during settle.
        Queue("first", 40); Dispatcher.UIThread.RunJobs(); Queue("last", 80); Queue("second", 20); Queue("third", 20); Queue("THIRD", 1);
        StartupLadder.PresentOrInbox(new InboxItem { Key = "passive", Open = () => calls.Add("passive") });
        Assert.Empty(calls);
        Assert.Equal(0, StartupLadder.Inbox.UnreadCount);
        clock.Advance(TimeSpan.FromMilliseconds(749)); Assert.Empty(calls);
        clock.Advance(TimeSpan.FromMilliseconds(1)); Assert.Equal(new[] { "first" }, calls);
        clock.Advance(TimeSpan.FromMilliseconds(2250));
        Assert.Equal(new[] { "first", "second", "third", "last", "passive" }, calls);
        Assert.Equal(1, drained);
        Assert.True(StartupLadder.IsLadderIdle);
    });

    [Fact]
    public Task ModalRemainsUpUntilAwaitedSurfaceClosesAndErrorsDoNotStrandQueue() => Run((owner, clock) =>
    {
        var close = new TaskCompletionSource();
        StartupLadder.EnqueueModal("dialog", 20, owner, () => close.Task);
        StartupLadder.EnqueueModal("broken", 30, owner, () => throw new InvalidOperationException("test"));
        Dispatcher.UIThread.RunJobs();
        clock.Advance(TimeSpan.FromMilliseconds(750));
        Assert.True(MainShellWindow.IsStartupDialogShowing);
        StartupLadder.PresentOrInbox(new InboxItem { Key = "during-modal" });
        Assert.Equal(1, StartupLadder.Inbox.UnreadCount);
        clock.Advance(TimeSpan.FromHours(1));
        Assert.False(StartupLadder.IsLadderIdle);
        close.SetResult(); Dispatcher.UIThread.RunJobs();
        clock.Advance(TimeSpan.FromMilliseconds(750));
        Assert.False(MainShellWindow.IsStartupDialogShowing);
        Assert.True(StartupLadder.IsLadderIdle);
    });

    [Fact]
    public Task LauncherParksWithoutSpendingTimeoutAndReleaseRunsOwedSurface() => Run((owner, clock) =>
    {
        int opened = 0, abandoned = 0;
        StartupLadder.Hold();
        StartupLadder.EnqueueModal("parked", 20, owner, () => { opened++; return Task.CompletedTask; }, () => abandoned++);
        StartupLadder.PresentOrInbox(new InboxItem { Key = "launcher-row" });
        Assert.Equal(1, StartupLadder.Inbox.UnreadCount);
        Dispatcher.UIThread.RunJobs();
        clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal((0, 0), (opened, abandoned));
        StartupLadder.Release(); clock.Advance(TimeSpan.FromMilliseconds(500));
        Assert.Equal((1, 0), (opened, abandoned));
    });

    [Fact]
    public Task UnreadyOwnerAbandonsExactKeyAfterFiveMinutes() => Run((owner, clock) =>
    {
        owner.Hide();
        var abandoned = new List<string>();
        StartupLadder.EnqueueModal("waiting", 50, owner, () => throw new Exception("must not open"), () => abandoned.Add("waiting"));
        Dispatcher.UIThread.RunJobs();
        clock.Advance(TimeSpan.FromMinutes(4));
        StartupLadder.EnqueueModal("higher", 10, owner, () => Task.CompletedTask, () => abandoned.Add("higher"));
        clock.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromMilliseconds(750));
        Assert.Equal(new[] { "waiting" }, abandoned);
        owner.Show(); clock.Advance(TimeSpan.FromMilliseconds(750));
        Assert.Equal(new[] { "waiting" }, abandoned);
        Assert.True(StartupLadder.IsLadderIdle);
    });

    [Fact]
    public Task RegisteredGameParksModalAndInboxesPassiveUntilItCloses() => Run((owner, clock) =>
    {
        var original = PanicSurfaces.All;
        bool gameUp = true;
        int opened = 0;
        try
        {
            PanicSurfaces.All = new[] { new PanicSurfaces.Surface("test-game", _ => gameUp = false, () => gameUp) };
            StartupLadder.EnqueueModal("game-parked", 20, owner, () => { opened++; return Task.CompletedTask; });
            StartupLadder.PresentOrInbox(new InboxItem { Key = "game-row" });
            Dispatcher.UIThread.RunJobs();
            clock.Advance(TimeSpan.FromHours(1));
            Assert.Equal(0, opened);
            Assert.Equal(1, StartupLadder.Inbox.UnreadCount);
            gameUp = false;
            clock.Advance(TimeSpan.FromMilliseconds(500));
            Assert.Equal(1, opened);
        }
        finally { PanicSurfaces.All = original; }
    });

    [Fact]
    public Task ForYouFeedDoesNotParkTheLadderButAGameDoes() => Run((owner, clock) =>
    {
        // WPF StartupPresenter.cs:373 SafeGameHostUp counts games only: the feed stays open for hours.
        var feed = new ConditioningControlPanel.Avalonia.Views.Games.GameWindow(
            ConditioningControlPanel.Avalonia.Views.Games.GameWindow.Games[ConditioningControlPanel.Avalonia.Views.Games.GameWindow.FypId]);
        ConditioningControlPanel.Avalonia.Views.Games.GameWindow? game = null;
        try
        {
            feed.Show();
            Assert.True(ConditioningControlPanel.Avalonia.Views.Games.GameWindow.IsAnyOpen());
            Assert.False(StartupLadder.World(clock.Now.UtcDateTime).GameHostUp);
            game = new ConditioningControlPanel.Avalonia.Views.Games.GameWindow();
            game.Show();
            Assert.True(StartupLadder.World(clock.Now.UtcDateTime).GameHostUp);
        }
        finally { feed.Close(); game?.Close(); }
    });

    [Fact]
    public Task QuietWatchReportsExpiryOnceAndStopsWithoutOpeningFiledRows() => Run((owner, clock) =>
    {
        var changes = new List<bool>();
        StartupLadder.QuietChanged += () => changes.Add(StartupLadder.IsQuiet);
        StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromSeconds(2));
        StartupLadder.PresentOrInbox(new InboxItem { Key = "quiet" });
        clock.Advance(TimeSpan.FromSeconds(1)); Assert.Equal(new[] { true }, changes);
        clock.Advance(TimeSpan.FromSeconds(1)); Assert.Equal(new[] { true, false }, changes);
        Assert.Equal(1, StartupLadder.Inbox.UnreadCount);
        Assert.Empty(clock.Timers);
    });
}
