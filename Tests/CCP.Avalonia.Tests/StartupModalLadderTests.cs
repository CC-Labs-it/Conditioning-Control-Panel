using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Startup;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class StartupModalLadderTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        StartupLadder.ResetForTests();
        StartupLadder.ModalWindowReady = () => true;
    }

    private static async Task Step()
    {
        Dispatcher.UIThread.RunJobs();
        ((StartupLadder.TestClock)StartupLadder.Time).Advance(StartupLadder.ModalSettle);
        for (var i = 0; i < 5; i++) await Task.Yield();
    }

    [Fact]
    public Task OffersRunInPriorityOrder() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        try
        {
            var order = new List<int>();
            foreach (var priority in new[] { 30, 10, 20 })
                StartupLadder.EnqueueModal(priority.ToString(), priority, () => { order.Add(priority); return Task.CompletedTask; });
            await Step(); await Step(); await Step();
            Assert.Equal(new[] { 10, 20, 30 }, order);
        }
        finally { StartupLadder.ResetForTests(); }
    });

    [Fact]
    public Task ArrivalDuringSettleWaitsForTheSelectedTurn() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        try
        {
            var order = new List<string>();
            StartupLadder.EnqueueModal("selected", 30, () => { order.Add("selected"); return Task.CompletedTask; });
            Dispatcher.UIThread.RunJobs(); // the pump has selected its turn
            StartupLadder.EnqueueModal("later", 10, () => { order.Add("later"); return Task.CompletedTask; });
            await Step();
            Assert.Equal(new[] { "selected" }, order);
            await Step();
            Assert.Equal(new[] { "selected", "later" }, order);
        }
        finally { StartupLadder.ResetForTests(); }
    });

    [Fact]
    public Task DeferredAndInboxOffersDoNotDuplicateEachOther() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var oldSession = CoreSession.IsSessionRunningProvider;
        try
        {
            var session = false;
            var opened = 0;
            CoreSession.IsSessionRunningProvider = () => session;
            var item = new InboxItem { Key = "card", Open = () => opened++ };
            StartupLadder.EnqueueModal("modal", 10, () => Task.CompletedTask);
            StartupLadder.PresentOrInbox(item);
            session = true;
            StartupLadder.PresentOrInbox(item);
            Assert.Equal(0, StartupLadder.Inbox.UnreadCount);
            session = false;
            await Step();
            Assert.Equal(1, opened);
            Assert.Equal(0, StartupLadder.Inbox.UnreadCount);
            ((StartupLadder.TestClock)StartupLadder.Time).Advance(StartupQueueCore.PassiveSettle);

            session = true;
            StartupLadder.PresentOrInbox(item);
            Assert.Equal(1, StartupLadder.Inbox.UnreadCount);
            session = false;
            StartupLadder.EnqueueModal("next", 10, () => Task.CompletedTask);
            StartupLadder.PresentOrInbox(item);
            await Step();
            Assert.Equal(1, opened);
            Assert.Equal(1, StartupLadder.Inbox.UnreadCount);
        }
        finally { CoreSession.IsSessionRunningProvider = oldSession; StartupLadder.ResetForTests(); }
    });

    [Fact]
    public Task WelcomeBackEntryWaitsBehindTheWizard() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var settings = CoreSettings.Current;
        var level = settings.PlayerLevel;
        var user = CoreAccount.UnifiedUserId;
        var dir = Directory.CreateTempSubdirectory("startup-welcome-").FullName;
        try
        {
            WelcomeBackOffer.ResetForTests();
            settings.PlayerLevel = 2;
            CoreAccount.UnifiedUserId = "returning-user";
            var closed = new TaskCompletionSource();
            var shown = 0;
            WelcomeBackOffer.ShowSheet = _ => { shown++; return Task.CompletedTask; };
            StartupLadder.EnqueueModal("first-run-wizard", 20, () => closed.Task);
            WelcomeBackOffer.Arm(true, dir);
            await WelcomeBackOffer.TryOfferAsync("startup");
            Assert.Equal(0, shown);
            await Step();
            Assert.True(MainShellWindow.IsStartupDialogShowing);
            Assert.Equal(0, shown);
            closed.SetResult();
            for (var i = 0; i < 5; i++) await Task.Yield();
            await Step();
            Assert.Equal(1, shown);
        }
        finally
        {
            MainShellWindow.IsStartupDialogShowing = false;
            WelcomeBackOffer.ResetForTests();
            settings.PlayerLevel = level;
            CoreAccount.UnifiedUserId = user;
            StartupLadder.ResetForTests();
            Directory.Delete(dir, true);
        }
    });

    [Fact]
    public Task DuplicateKeysAreOfferedOnceAndTheScreenGateIsRespected() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        try
        {
            var ready = false;
            var calls = 0;
            StartupLadder.ModalWindowReady = () => ready;
            StartupLadder.EnqueueModal("same", 10, () => { calls++; return Task.CompletedTask; });
            StartupLadder.EnqueueModal("SAME", 1, () => { calls += 100; return Task.CompletedTask; });
            await Step();
            Assert.Equal(0, calls);
            ready = true;
            ((StartupLadder.TestClock)StartupLadder.Time).Advance(StartupLadder.ModalPoll);
            for (var i = 0; i < 5; i++) await Task.Yield();
            Assert.Equal(1, calls);
        }
        finally { StartupLadder.ResetForTests(); }
    });

    [Fact]
    public Task UnavailableScreenEventuallyAbandonsTheOffer() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        try
        {
            var shown = false;
            var abandoned = false;
            StartupLadder.ModalWindowReady = () => false;
            StartupLadder.EnqueueModal("blocked", 10, () => { shown = true; return Task.CompletedTask; }, () => abandoned = true);
            await Step();
            var clock = (StartupLadder.TestClock)StartupLadder.Time;
            for (var i = 0; i < (int)(StartupLadder.ModalMaxWait / StartupLadder.ModalPoll); i++)
            { clock.Advance(StartupLadder.ModalPoll); for (var j = 0; j < 5; j++) await Task.Yield(); }
            Assert.False(shown);
            Assert.True(abandoned);
            foreach (var game in new[] { false, true })
            {
                var panel = game ? null : new MainShellWindow();
                global::Avalonia.Controls.Window surface = game ? new ConditioningControlPanel.Avalonia.Views.Games.GameWindow() : LauncherWindow.OpenFor(panel!);
                if (game) surface.Show(); else StartupLadder.Hold();   // WPF RouteBoot holds the ladder behind the launcher
                shown = abandoned = false; StartupLadder.Inbox.Items.Clear(); StartupLadder.ModalWindowReady = () => true;
                StartupLadder.EnqueueModal("parked", 10, () => { shown = true; return Task.CompletedTask; }, () => abandoned = true);
                StartupLadder.PresentOrInbox(new InboxItem { Key = "passive", Open = () => throw new InvalidOperationException() });
                Assert.True(StartupLadder.Inbox.Contains("passive")); await Step();
                for (var i = 0; i < 610; i++) { clock.Advance(StartupLadder.ModalPoll); for (var j = 0; j < 5; j++) await Task.Yield(); }
                Assert.False(shown); Assert.False(abandoned);
                if (!game) { surface.Hide(); StartupLadder.ModalWindowReady = () => false; Assert.True(StartupLadder.World(clock.Now.UtcDateTime).LauncherHolding); panel!.ShowFromTray(); StartupLadder.Release(); }
                if (game) surface.Close(); StartupLadder.ModalWindowReady = () => game || panel!.IsVisible; clock.Advance(StartupLadder.ModalPoll);
                for (var i = 0; i < 5; i++) await Task.Yield();
                Assert.True(shown); Assert.False(abandoned);
                if (!game) surface.Close(); panel?.Close();
            }
        }
        finally { StartupLadder.ResetForTests(); }
    });

    [Fact]
    public Task ModalOwnsScreenUntilItsTaskCompletes() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        try
        {
            var closed = new TaskCompletionSource();
            var second = false;
            StartupLadder.EnqueueModal("first", 10, () => closed.Task);
            StartupLadder.EnqueueModal("second", 20, () => { second = true; return Task.CompletedTask; });
            await Step();
            Assert.True(MainShellWindow.IsStartupDialogShowing);
            await Step();
            Assert.False(second);
            closed.SetResult();
            for (var i = 0; i < 5; i++) await Task.Yield();
            await Step();
            Assert.True(second);
            Assert.False(MainShellWindow.IsStartupDialogShowing);
        }
        finally { MainShellWindow.IsStartupDialogShowing = false; StartupLadder.ResetForTests(); }
    });

    [Fact]
    public Task FailureReleasesOwnershipAndRunsNext() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        try
        {
            var second = false;
            StartupLadder.EnqueueModal("failure", 10, () => throw new InvalidOperationException());
            StartupLadder.EnqueueModal("next", 20, () => { second = true; return Task.CompletedTask; });
            await Step();
            Assert.False(MainShellWindow.IsStartupDialogShowing);
            await Step();
            Assert.True(second);
            Assert.False(MainShellWindow.IsStartupDialogShowing);
            var calls = 0; var stranded = false;   // a pump that throws restarts while entries remain
            StartupLadder.ModalWindowReady = () => ++calls > 1 ? true : throw new InvalidOperationException();
            StartupLadder.EnqueueModal("stranded", 10, () => { stranded = true; return Task.CompletedTask; });
            await Step(); await Step();
            Assert.True(stranded);
        }
        finally { MainShellWindow.IsStartupDialogShowing = false; StartupLadder.ResetForTests(); }
    });

    [Fact]
    public Task PassiveSurfaceWaitsForQueuedModalThenIsReevaluated() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        try
        {
            var opened = 0;
            StartupLadder.EnqueueModal("modal", 10, () => Task.CompletedTask);
            StartupLadder.PresentOrInbox(new InboxItem { Key = "passive", Open = () => opened++ });
            Assert.Equal(0, opened);
            Assert.Equal(0, StartupLadder.Inbox.UnreadCount);
            await Step();
            Assert.Equal(1, opened);
            Assert.Equal(0, StartupLadder.Inbox.UnreadCount);
        }
        finally { StartupLadder.ResetForTests(); }
    });
}
