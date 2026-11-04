using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
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

    [Theory]
    [InlineData("fresh")]
    [InlineData("unchanged")]
    [InlineData("priority")]
    [InlineData("persist")]
    public Task WhatsNewStartup(string scenario) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        try
        {
            var settings = new ConditioningControlPanel.Models.AppSettings();
            settings.LastSeenVersion = scenario == "fresh" ? "" : scenario == "unchanged" ? "2" : "1";
            var saves = 0;
            var order = new List<int>();
            Func<Task> show = () => { Assert.True(MainShellWindow.IsStartupDialogShowing); order.Add(30); return Task.CompletedTask; };
            MainShellWindow.OfferWhatsNewIfNeeded(settings, "2", () => saves++, show);
            if (scenario == "fresh")
            {
                Assert.Equal("2", settings.LastSeenVersion);
                Assert.Equal(1, saves);
            }
            if (scenario == "priority")
            {
                StartupLadder.EnqueueModal("earlier", 20, () => { order.Add(20); return Task.CompletedTask; });
                StartupLadder.EnqueueModal("later", 40, () => { order.Add(40); return Task.CompletedTask; });
            }
            if (scenario == "persist") Assert.Equal("1", settings.LastSeenVersion);
            await Step(); await Step(); await Step();
            if (scenario == "fresh" || scenario == "unchanged") Assert.Empty(order);
            if (scenario == "unchanged") Assert.Equal(0, saves);
            if (scenario == "priority") Assert.Equal(new[] { 20, 30, 40 }, order);
            if (scenario == "persist")
            {
                Assert.Equal(new[] { 30 }, order);
                Assert.Equal("2", settings.LastSeenVersion);
                Assert.Equal(1, saves);
                Assert.False(MainShellWindow.IsStartupDialogShowing);
            }
        }
        finally { MainShellWindow.IsStartupDialogShowing = false; StartupLadder.ResetForTests(); }
    });

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
            {
                clock.Advance(StartupLadder.ModalPoll);
                for (var j = 0; j < 5; j++) await Task.Yield();
            }
            Assert.False(shown);
            Assert.True(abandoned);
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
