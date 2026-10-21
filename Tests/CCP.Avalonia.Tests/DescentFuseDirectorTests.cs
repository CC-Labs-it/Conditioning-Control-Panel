using System;
using System.Linq;
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
    // WPF DescentFuseWindow.ShowsRetired (DescentFuseWindow.xaml.cs:173): every show refuses to open,
    // so nothing is marked played and no retry starts behind a show that never was.
    [Theory]
    [InlineData("startup-catch-up")]
    [InlineData("late-catch-up")]
    [InlineData("live")]
    public Task RetiredShowsOpenNothing_MarkNothing_AndStartNoRetry(string path) => Run(path != "startup-catch-up", (shell, fuse, s) =>
    {
        shell.Show(); Dispatcher.UIThread.RunJobs();
        if (path != "startup-catch-up")
        {
            s.DescentCeremonyAtUtc = DateTime.UtcNow.Add(path == "live" ? TimeSpan.FromSeconds(-1) : TimeSpan.FromHours(-1)).ToString("o");
            fuse.OnTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
        Assert.Equal(path != "live", fuse.ShouldPlayCatchUp);
        Assert.Null(DescentFuseWindow.Open(DescentShowKind.Live));
        Assert.Empty(DescentFuseWindow.OnScreen);
        Assert.False(s.DescentCatchUpCrackPlayed);
        Assert.False(s.DescentLastNightWitnessed);
        Assert.False(shell.FuseRetryRunning);
    });

    [Fact]
    public Task LateZeroWithNoCrackOwedRetriesBoundedly() => Run(true, (shell, fuse, s) =>
    {
        LateZeroNoCrackOwed(shell, fuse, s);
        Assert.Empty(DescentFuseWindow.OnScreen);
        Assert.True(shell.FuseRetryRunning);
        for (var i = 0; i < DescentPostZeroRetry.MaxAttempts; i++) shell.TickFuseRetry();
        Assert.Equal(DescentPostZeroRetry.MaxAttempts, shell.FuseRetryAttempts);
        shell.TickFuseRetry();
        Assert.False(shell.FuseRetryRunning);
    });

    [Theory]
    [InlineData("completed")]
    [InlineData("pending")]
    [InlineData("dark")]
    public Task RetryStopsWhenTheAccountAnswersOrTheFuseGoesDark(string reason) => Run(true, (shell, fuse, s) =>
    {
        LateZeroNoCrackOwed(shell, fuse, s);
        Assert.True(shell.FuseRetryRunning);
        if (reason == "completed") s.DescentMigrationCompleted = true;
        else if (reason == "pending") s.PendingDescentMigrationChoice = DescentMigrationChoices.Restore;
        else fuse.ApplyCeremonyAt(null);
        shell.TickFuseRetry();
        Assert.False(shell.FuseRetryRunning);
        Assert.Equal(0, shell.FuseRetryAttempts);
    });

    [Fact]
    public Task PanicClosesAnyShow() => Run(true, (shell, fuse, s) =>
    {
        shell.Show();
        Assert.NotNull(DescentFuseWindow.OpenShow(DescentShowKind.Live));
        Assert.Null(DescentFuseWindow.OpenShow(DescentShowKind.CatchUp)); // single-open guard
        PanicSurfaces.StopAll("fuse-test", shell);
        Assert.Empty(DescentFuseWindow.OnScreen);
    });

    // Sleep across zero after the catch-up already played: WPF DescentShowDirector.cs:192-196.
    private static void LateZeroNoCrackOwed(MainShellWindow shell, DescentCountdownService fuse, AppSettings s)
    {
        s.DescentCatchUpCrackPlayed = true;
        shell.Show(); Dispatcher.UIThread.RunJobs();
        s.DescentCeremonyAtUtc = DateTime.UtcNow.AddHours(-1).ToString("o");
        fuse.OnTimerTick();
        Dispatcher.UIThread.RunJobs();
        Assert.False(fuse.ShouldPlayCatchUp);
    }

    private static Task Run(bool future, Action<MainShellWindow, DescentCountdownService, AppSettings> body) =>
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
        var at = now.AddHours(-1).ToString("o");
        s.DescentCeremonyAtUtc = now.AddHours(1).ToString("o");
        s.DescentCatchUpCrackPlayed = true; // late zero, no crack owed: the one path WPF still retries
        s.DescentLastNightWitnessed = false;
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
            s.DescentCeremonyAtUtc = at;
            fuse.OnTimerTick();
            Assert.True(shell.FuseRetryRunning);
            var before = wire.Syncs.Count();
            s.OfflineMode = false; // only the injected HTTP handler may service the sync
            await shell.TickFuseRetry();
            Assert.Equal(before + 1, wire.Syncs.Count());
            await MainShellWindow.RequestFuseSync(); // sync cooldown owns refusal, just as WPF does
            Assert.Equal(before + 1, wire.Syncs.Count());

            now = now.AddMinutes(1);
            var live = DescentFuseWindow.OpenShow(DescentShowKind.Live)!; // the retired show still resyncs at its bloom
            live.FrameCrack(DescentFuseTimeline.LiveSeconds); // reduced/full both reached bloom
            await live.LastResync;
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
