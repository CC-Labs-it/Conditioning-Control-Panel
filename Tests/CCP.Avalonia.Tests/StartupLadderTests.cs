using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using ConditioningControlPanel;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Startup;
using Xunit;

namespace CCP.Avalonia.Tests;

// No windows, fonts, desktop lifetime or profile: exercise the real head router on its dispatcher.
public sealed class StartupLadderTests
{
    private static Task Run(Action body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        StartupLadder.ResetForTests();
        MainShellWindow.IsStartupDialogShowing = false;
        var tutorial = CoreTutorial.IsActiveProvider;
        var session = CoreSession.IsSessionRunningProvider;
        CoreTutorial.IsActiveProvider = () => false;
        CoreSession.IsSessionRunningProvider = () => false;
        StartupLadder.ModalOwner = () => null;
        StartupLadder.WindowReady = () => true;
        try { body(); }
        finally
        {
            MainShellWindow.IsStartupDialogShowing = false;
            CoreTutorial.IsActiveProvider = tutorial;
            CoreSession.IsSessionRunningProvider = session;
            StartupLadder.ResetForTests();
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ActualStartupModalFilesPassiveCard() => Run(() =>
    {
        MainShellWindow.IsStartupDialogShowing = true;
        var opened = false;
        StartupLadder.PresentOrInbox(new InboxItem { Key = "modal-card", Open = () => opened = true });
        Assert.False(opened);
        Assert.Equal("modal-card", Assert.Single(StartupLadder.Inbox.Items).Key);
        MainShellWindow.IsStartupDialogShowing = false;
        StartupLadder.Inbox.Open(StartupLadder.Inbox.Items[0]);
        Assert.True(opened);
    });

    private static StartupLadder.TestClock Clock => (StartupLadder.TestClock)StartupLadder.Time;
    private static void Start() => Dispatcher.UIThread.RunJobs();

    [Fact]
    public Task QueueOrdersDeduplicatesAndSettlesEachTurn() => Run(() =>
    {
        var order = new List<string>();
        foreach (var (key, priority) in new[] { ("update", 80), ("welcome", 30), ("age", 5), ("recap", 30), ("AGE", 1) })
            StartupLadder.EnqueueModal(key, priority, _ => { order.Add(key); return Task.CompletedTask; });
        Start();
        Clock.Advance(TimeSpan.FromMilliseconds(749)); Assert.Empty(order);
        Clock.Advance(TimeSpan.FromMilliseconds(1)); Assert.Equal(new[] { "age" }, order);
        Clock.Advance(TimeSpan.FromMilliseconds(2250));
        Assert.Equal(new[] { "age", "welcome", "recap", "update" }, order);
        Assert.True(StartupLadder.IsLadderIdle);
        Assert.False(MainShellWindow.IsStartupDialogShowing);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task BusyLadderDefersThenReasksBeforeDrained(bool tourStarts) => Run(() =>
    {
        var opened = false;
        StartupLadder.EnqueueModal("predicate", 30, _ =>
        {
            CoreTutorial.IsActiveProvider = () => tourStarts;
            return Task.CompletedTask;
        });
        StartupLadder.PresentOrInbox(new InboxItem { Key = "deferred", Open = () => opened = true });
        StartupLadder.PresentOrInbox(new InboxItem { Key = "DEFERRED", Open = () => throw new Exception("duplicate") });
        Assert.False(opened); Assert.Empty(StartupLadder.Inbox.Items);
        var drained = false;
        StartupLadder.Drained += () =>
        {
            drained = true;
            Assert.Equal(!tourStarts, opened);
            Assert.Equal(tourStarts ? 1 : 0, StartupLadder.Inbox.UnreadCount);
        };
        Start(); Clock.Advance(TimeSpan.FromMilliseconds(750));
        Assert.True(drained);
    });

    [Fact]
    public Task DrainRearmsPassiveWatchWhenAnEarlierCardIsStillSettling() => Run(() =>
    {
        StartupLadder.PresentOrInbox(new InboxItem { Key = "earlier" });
        StartupLadder.EnqueueModal("predicate", 30, _ => Task.CompletedTask);
        var opened = false;
        StartupLadder.PresentOrInbox(new InboxItem { Key = "deferred", Open = () => opened = true });
        Start(); Clock.Advance(TimeSpan.FromMilliseconds(750));
        Assert.False(opened);
        Clock.Advance(TimeSpan.FromMilliseconds(2999)); Assert.False(opened);
        Clock.Advance(TimeSpan.FromMilliseconds(1)); Assert.True(opened);
        Assert.Empty(Clock.Timers);
    });

    [Fact]
    public Task AwaitedModalOwnsFlagUntilClosedAndFilesArrivals() => Run(() =>
    {
        var closed = new TaskCompletionSource();
        var second = false;
        StartupLadder.EnqueueModal("first", 10, _ => closed.Task);
        StartupLadder.EnqueueModal("second", 20, _ => { second = true; return Task.CompletedTask; });
        Start(); Clock.Advance(TimeSpan.FromMilliseconds(750));
        Assert.True(StartupLadder.IsModalUp);
        StartupLadder.PresentOrInbox(new InboxItem { Key = "during", Open = () => throw new Exception("interrupted") });
        Assert.Single(StartupLadder.Inbox.Items);
        Clock.Advance(TimeSpan.FromMinutes(1)); Assert.False(second);
        closed.SetResult(); Start();
        Assert.False(StartupLadder.IsModalUp);
        Clock.Advance(TimeSpan.FromMilliseconds(750)); Assert.True(second);
    });

    [Fact]
    public Task LauncherParksWithoutSpendingTimeoutThenReleases() => Run(() =>
    {
        var shown = false; var abandoned = false;
        StartupLadder.Hold();
        StartupLadder.EnqueueModal("held", 10, _ => { shown = true; return Task.CompletedTask; }, () => abandoned = true);
        StartupLadder.PresentOrInbox(new InboxItem { Key = "launcher-card" });
        Start(); Clock.Advance(TimeSpan.FromMinutes(10));
        Assert.False(shown); Assert.False(abandoned); Assert.True(StartupLadder.IsQuiet);
        Assert.Single(StartupLadder.Inbox.Items);
        StartupLadder.Release(); Clock.Advance(TimeSpan.FromMilliseconds(500));
        Assert.True(shown); Assert.False(abandoned);
    });

    [Fact]
    public Task TimeoutBelongsToSelectedKeyAndFailuresDoNotStallQueue() => Run(() =>
    {
        var abandoned = new List<string>(); var shown = false;
        StartupLadder.WindowReady = () => false;
        StartupLadder.EnqueueModal("waiting", 80, _ => throw new Exception("must abandon"),
            () => { abandoned.Add("waiting"); throw new Exception("bookkeeping failed"); });
        Start(); Clock.Advance(TimeSpan.FromMilliseconds(750));
        Clock.Advance(TimeSpan.FromMinutes(4));
        StartupLadder.EnqueueModal("new-high-priority", 5, _ => { shown = true; throw new Exception("show failed"); },
            () => abandoned.Add("wrong-key"));
        Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(new[] { "waiting" }, abandoned); Assert.False(shown);
        StartupLadder.WindowReady = () => true;
        Clock.Advance(TimeSpan.FromMilliseconds(750));
        Assert.True(shown); Assert.True(StartupLadder.IsLadderIdle);
        Assert.False(MainShellWindow.IsStartupDialogShowing);
    });

    [Fact]
    public Task QuietWatchAnnouncesExpiryWithoutOpeningUnreadRowsAndStops() => Run(() =>
    {
        var changes = new List<bool>();
        StartupLadder.QuietChanged += () => changes.Add(StartupLadder.IsQuiet);
        StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromSeconds(2));
        StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromSeconds(1)); // never shorten it
        StartupLadder.PresentOrInbox(new InboxItem { Key = "unread", Open = () => throw new Exception("unasked") });
        Clock.Advance(TimeSpan.FromSeconds(1)); Assert.True(StartupLadder.IsQuiet);
        Clock.Advance(TimeSpan.FromSeconds(1)); Assert.False(StartupLadder.IsQuiet);
        Assert.Equal(new[] { true, false }, changes);
        Assert.Single(StartupLadder.Inbox.Items); Assert.Empty(Clock.Timers);
    });

    [Fact]
    public Task SessionWatchSeesExternalEndAndPanicFoldsHeldCards() => Run(() =>
    {
        var running = true;
        CoreSession.IsSessionRunningProvider = () => running;
        var changes = new List<bool>();
        StartupLadder.QuietChanged += () => changes.Add(StartupLadder.IsQuiet);
        StartupLadder.PresentOrInbox(new InboxItem { Key = "session" });
        Clock.Advance(TimeSpan.FromSeconds(1));
        running = false; Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(new[] { true, false }, changes);
        StartupLadder.PresentOrInbox(new InboxItem { Key = "first" });
        StartupLadder.PresentOrInbox(new InboxItem { Key = "held", Open = () => throw new Exception("after panic") });
        PanicSurfaces.All.Single(s => s.Id == "startup-passive").Stop(null);
        Clock.Advance(TimeSpan.FromSeconds(4));
        Assert.Contains(StartupLadder.Inbox.Items, i => i.Key == "held");
    });
}
