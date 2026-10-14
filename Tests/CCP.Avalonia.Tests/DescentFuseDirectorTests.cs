using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Descent;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class DescentFuseDirectorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task StartupAndSleepAcrossZeroOpenCatchUp_AndMarkOnlyAfterClose(bool late) => Run(late, (shell, fuse, s) =>
    {
        shell.Show();
        if (late)
        {
            s.DescentCeremonyAtUtc = DateTime.UtcNow.AddHours(-1).ToString("o");
            fuse.OnTimerTick();
        }
        Dispatcher.UIThread.RunJobs();
        var show = Assert.Single(DescentFuseWindow.OnScreen);
        Assert.Equal(DescentShowKind.CatchUp, typeof(DescentFuseWindow)
            .GetField("_kind", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(show));
        Assert.False(s.DescentCatchUpCrackPlayed);
        Assert.False(s.DescentLastNightWitnessed);
        show.Close();
        Assert.True(s.DescentCatchUpCrackPlayed);
        Assert.False(s.DescentLastNightWitnessed);
        Assert.True(shell.FuseRetryRunning);
        for (var i = 0; i < DescentPostZeroRetry.MaxAttempts; i++) shell.TickFuseRetry();
        Assert.Equal(DescentPostZeroRetry.MaxAttempts, shell.FuseRetryAttempts);
        shell.TickFuseRetry();
        Assert.False(shell.FuseRetryRunning);
        var next = new MainShellWindow();
        try { next.Show(); Dispatcher.UIThread.RunJobs(); Assert.Empty(DescentFuseWindow.OnScreen); }
        finally { next.Close(); }
    });

    [Fact]
    public Task LiveZeroRaisedDuringStartupBeforeTheShellExistsIsNotLost() => Run(true, (shell, fuse, s) =>
    {
        shell.Show(); Dispatcher.UIThread.RunJobs();
        var show = Assert.Single(DescentFuseWindow.OnScreen);
        Assert.Equal(DescentShowKind.Live, typeof(DescentFuseWindow)
            .GetField("_kind", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(show));
        fuse.OnTimerTick();
        Dispatcher.UIThread.RunJobs();
        Assert.Same(show, Assert.Single(DescentFuseWindow.OnScreen));
        show.Close();
        fuse.ApplyCeremonyAt(DateTime.UtcNow.AddHours(2).ToString("o"));
        s.DescentCeremonyAtUtc = DateTime.UtcNow.AddSeconds(-2).ToString("o");
        fuse.OnTimerTick(); // the owner's postponed instant still gets its own live zero
        Assert.NotSame(show, Assert.Single(DescentFuseWindow.OnScreen));
    }, ZeroBeforeShell);

    [Fact]
    public Task PanicDropsAQueuedStartupLiveZero() => Run(true, (shell, fuse, s) =>
    {
        PanicSurfaces.StopAll("startup-live-test", shell);
        shell.Show(); Dispatcher.UIThread.RunJobs();
        Assert.Empty(DescentFuseWindow.OnScreen);
    }, ZeroBeforeShell);

    private static void ZeroBeforeShell(DescentCountdownService fuse, AppSettings s)
    {
        s.DescentCeremonyAtUtc = DateTime.UtcNow.AddSeconds(-1).ToString("o");
        fuse.OnTimerTick();
        Assert.False(fuse.ZeroPassedWhileAway);
        Assert.Equal(DescentFusePhase.Zero, fuse.LastAnnouncedPhase);
    }

    [Fact]
    public Task PanicClosesTheLiveShow() => Run(true, (shell, fuse, s) =>
    {
        shell.Show();
        Assert.NotNull(MainShellWindow.OpenLiveFuseShow());
        PanicSurfaces.StopAll("fuse-test", shell);
        Assert.Empty(DescentFuseWindow.OnScreen);
    });

    [Fact]
    public Task PanicDropsAQueuedCatchUpWithoutMarkingItPlayed() => Run(false, (shell, fuse, s) =>
    {
        PanicSurfaces.StopAll("queued-fuse-test", shell);
        shell.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(DescentFuseWindow.OnScreen);
        Assert.False(s.DescentCatchUpCrackPlayed);
    });

    [Theory]
    [InlineData("completed")]
    [InlineData("pending")]
    [InlineData("dark")]
    public Task RetryStopsWhenTheAccountAnswersOrTheFuseGoesDark(string reason) => Run(false, (shell, fuse, s) =>
    {
        shell.Show(); Dispatcher.UIThread.RunJobs();
        Assert.Single(DescentFuseWindow.OnScreen).Close();
        Assert.True(shell.FuseRetryRunning);
        if (reason == "completed") s.DescentMigrationCompleted = true;
        else if (reason == "pending") s.PendingDescentMigrationChoice = DescentMigrationChoices.Restore;
        else fuse.ApplyCeremonyAt(null);
        shell.TickFuseRetry();
        Assert.False(shell.FuseRetryRunning);
        Assert.Equal(0, shell.FuseRetryAttempts);
    });

    [Fact]
    public Task AnExistingShowPreventsCatchUpAndDoesNotMarkItPlayed() => Run(false, (shell, fuse, s) =>
    {
        Assert.NotNull(MainShellWindow.OpenLiveFuseShow());
        shell.Show(); Dispatcher.UIThread.RunJobs();
        Assert.Single(DescentFuseWindow.OnScreen);
        Assert.False(s.DescentCatchUpCrackPlayed);
        DescentFuseWindow.ForceCloseAll();
        Assert.False(s.DescentCatchUpCrackPlayed);
    });

    private static Task Run(bool future, Action<MainShellWindow, DescentCountdownService, AppSettings> body,
        Action<DescentCountdownService, AppSettings>? beforeShell = null) =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var previous = CoreSettings.ServiceProvider;
            var oldSync = ConditioningControlPanel.Avalonia.Platform.AccountSeed.Sync;
            ConditioningControlPanel.Avalonia.Platform.AccountSeed.Sync = null;
            var oldFuse = AvApp.DescentCountdown;
            var settings = new SettingsService();
            CoreSettings.ServiceProvider = () => settings;
            var s = settings.Current;
            (s.AvatarEnabled, s.OfflineMode, s.Welcomed, s.HasAcceptedAgeVerification) = (false, true, true, true);
            s.WhatMovedCardShown = 1;
            s.DescentCeremonyAtUtc = DateTime.UtcNow.AddHours(future ? 1 : -1).ToString("o");
            s.DescentMigrationCompleted = s.DescentLastNightWitnessed = s.DescentCatchUpCrackPlayed = false;
            s.PendingDescentMigrationChoice = null;
            s.MotionLevel = MotionLevel.Reduced;
            using var fuse = new DescentCountdownService();
            AvApp.DescentCountdown = fuse;
            fuse.Start();
            MainShellWindow? shell = null;
            try
            {
                beforeShell?.Invoke(fuse, s);
                shell = new MainShellWindow();
                body(shell, fuse, s);
            }
            finally
            {
                DescentFuseWindow.ForceCloseAll();
                shell?.Close();
                AvApp.DescentCountdown = oldFuse;
                ConditioningControlPanel.Avalonia.Platform.AccountSeed.Sync = oldSync;
                settings.SaveImmediate();
                CoreSettings.ServiceProvider = previous;
            }
            return Task.CompletedTask;
        });
}

public sealed partial class AccountSeedTests
{
    [Fact]
    public void FuseBloomAndPostShowRetryUseTheExistingSync() => WithFreshInstall(() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var old = (s.DescentCeremonyAtUtc, s.DescentCatchUpCrackPlayed, s.DescentLastNightWitnessed, s.MotionLevel, s.AvatarEnabled, s.PendingDescentMigrationChoice);
        var oldFuse = AvApp.DescentCountdown;
        var (wire, sync, _) = SignIn("fuse-user", L40Profile("fuse-user"));
        sync.MarkLoaded(Array.Empty<string>());
        var now = DateTime.UtcNow;
        sync.UtcNow = () => now;
        // Keep the timestamp armed: an absent countdown on the fake response is a real kill switch.
        var at = now.AddSeconds(-1).ToString("o");
        s.DescentCeremonyAtUtc = at;
        s.DescentCatchUpCrackPlayed = s.DescentLastNightWitnessed = false;
        s.PendingDescentMigrationChoice = null;
        s.MotionLevel = MotionLevel.Reduced;
        s.AvatarEnabled = false;
        wire.SyncReply = "{\"success\":true,\"descent_countdown\":{\"ceremony_at\":\"" + at + "\"}}";
        using var fuse = new DescentCountdownService();
        AvApp.DescentCountdown = fuse;
        s.OfflineMode = true; // countdown config is not the fake SyncPush endpoint
        fuse.Start();
        MainShellWindow? shell = null;
        try
        {
            shell = new MainShellWindow();
            shell.Show(); Dispatcher.UIThread.RunJobs();
            Assert.Single(DescentFuseWindow.OnScreen).Close();
            Assert.True(shell.FuseRetryRunning);
            var before = wire.Syncs.Count();
            s.OfflineMode = false; // only the injected HTTP handler may service the sync
            shell.TickFuseRetry();
            for (var i = 0; i < 100 && wire.Syncs.Count() == before; i++) await Task.Delay(10);
            Assert.Equal(before + 1, wire.Syncs.Count());
            await MainShellWindow.RequestFuseSync(); // sync cooldown owns refusal, just as WPF does
            Assert.Equal(before + 1, wire.Syncs.Count());

            now = now.AddMinutes(1);
            var live = MainShellWindow.OpenLiveFuseShow()!;
            live.FrameCrack(DescentFuseTimeline.LiveSeconds); // reduced/full both reached bloom
            for (var i = 0; i < 100 && wire.Syncs.Count() < before + 2; i++) await Task.Delay(10);
            Assert.Equal(before + 2, wire.Syncs.Count());
            live.FrameCrack(DescentFuseTimeline.LiveSeconds + 0.01);
            Assert.Equal(before + 2, wire.Syncs.Count()); // one sync per handoff slot
        }
        finally
        {
            DescentFuseWindow.ForceCloseAll();
            shell?.Close();
            AvApp.DescentCountdown = oldFuse;
            (s.DescentCeremonyAtUtc, s.DescentCatchUpCrackPlayed, s.DescentLastNightWitnessed, s.MotionLevel, s.AvatarEnabled, s.PendingDescentMigrationChoice) = old;
        }
    }));
}
