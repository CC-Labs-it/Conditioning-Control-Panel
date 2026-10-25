using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class FeatureIntroUpgradeTests
{
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public Task FreshAndCurrentVersionsStaySilent(string? previous) => Run(previous, async shell =>
    {
        await shell.ShowWhatsNewIfNeeded();
        Assert.Empty(shell.OwnedWindows.OfType<WhatsNewDialog>());
        Assert.Equal(CoreReleaseContent.AppVersion, CoreSettings.Current.LastSeenVersion);
    });

    [Fact]
    public Task UpgradeHoldsCardsAndStampsOnlyAfterDismissal() => Run("old-version", async shell =>
    {
        var showing = shell.ShowWhatsNewIfNeeded();
        var news = Assert.Single(shell.OwnedWindows.OfType<WhatsNewDialog>());
        Assert.True(MainShellWindow.IsStartupDialogShowing);
        Assert.Equal("old-version", CoreSettings.Current.LastSeenVersion);
        Assert.Equal($"What's New in v{CoreReleaseContent.AppVersion}", news.FindControl<TextBlock>("TxtTitle")!.Text);
        Assert.Equal(CoreReleaseContent.PatchNotes, news.FindControl<TextBlock>("TxtNotes")!.Text);
        Assert.True(news.FindControl<Button>("BtnTour")!.IsVisible);
        Assert.Equal("Show me around (60s)", news.FindControl<TextBlock>("TourLabel")!.Text);
        FeatureIntroPopup.ShowWhenStartupSettles("lockdown", shell, "play");
        Assert.True(StartupLadder.Inbox.Contains("intro:lockdown"));
        Assert.DoesNotContain("lockdown", CoreSettings.Current.SeenFeatureIntros);
        Assert.Empty(shell.OwnedWindows.OfType<FeatureIntroPopup>());
        news.Close();
        await showing;
        Assert.False(MainShellWindow.IsStartupDialogShowing);
        Assert.Equal(CoreReleaseContent.AppVersion, CoreSettings.Current.LastSeenVersion);
        await shell.ShowWhatsNewIfNeeded();
        Assert.Empty(shell.OwnedWindows.OfType<WhatsNewDialog>());
    });

    [Fact]
    public Task BusyStartupAbandonsUnspent() => Run("old-version", async shell =>
    {
        MainShellWindow.IsStartupDialogShowing = true;
        var delays = new List<TimeSpan>();
        shell.UpgradeDelay = delay => { delays.Add(delay); return Task.CompletedTask; };
        await shell.ShowWhatsNewIfNeeded(); // stepped delay reaches the WPF five-minute deadline
        Assert.Empty(shell.OwnedWindows.OfType<WhatsNewDialog>());
        Assert.Equal("old-version", CoreSettings.Current.LastSeenVersion);
        Assert.True(MainShellWindow.IsStartupDialogShowing); // somebody else's flag stays theirs
        Assert.Equal(TimeSpan.FromMilliseconds(750), delays[0]);
        Assert.Equal(601, delays.Count);
        Assert.All(delays.Skip(1), delay => Assert.Equal(TimeSpan.FromMilliseconds(500), delay));
    });

    [Fact]
    public Task PanicClosesUpgradeAndReleasesModalGate() => Run("old-version", async shell =>
    {
        var showing = shell.ShowWhatsNewIfNeeded();
        Assert.Single(shell.OwnedWindows.OfType<WhatsNewDialog>());
        PanicSurfaces.All.Single(s => s.Id == "upgrade-intro").Stop(shell);
        await showing;
        Assert.False(MainShellWindow.IsStartupDialogShowing);
        Assert.Empty(shell.OwnedWindows.OfType<WhatsNewDialog>());
    });

    [Fact]
    public Task RealShellOpenedCallbackReachesUpgradeDialog() => Run("old-version", async shell =>
    {
        Dispatcher.UIThread.RunJobs();
        var news = Assert.Single(shell.OwnedWindows.OfType<WhatsNewDialog>());
        Assert.True(MainShellWindow.IsStartupDialogShowing);
        news.Close();
        await Task.Yield();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(CoreReleaseContent.AppVersion, CoreSettings.Current.LastSeenVersion);
    }, detachStartup: false);

    [Fact]
    public Task QueuedUpgradeDefersFeatureCardWithoutSpendingIt() => Run("old-version", async shell =>
    {
        var settle = new TaskCompletionSource();
        shell.UpgradeDelay = _ => settle.Task;
        var showing = shell.ShowWhatsNewIfNeeded();
        FeatureIntroPopup.ShowWhenStartupSettles("lockdown", shell, "play");
        Assert.False(StartupLadder.Inbox.Contains("intro:lockdown"));
        Assert.DoesNotContain("lockdown", CoreSettings.Current.SeenFeatureIntros);
        Assert.Empty(shell.OwnedWindows.OfType<FeatureIntroPopup>());
        settle.SetResult();
        await Task.Yield();
        Dispatcher.UIThread.RunJobs();
        Assert.Single(shell.OwnedWindows.OfType<WhatsNewDialog>()).Close();
        await showing;
        Assert.False(StartupLadder.ModalPending);
        StartupLadder.PumpHeld();
        Dispatcher.UIThread.RunJobs();
        Assert.Single(shell.OwnedWindows.OfType<FeatureIntroPopup>());
        Assert.Contains("lockdown", CoreSettings.Current.SeenFeatureIntros);
    });

    private static Task Run(string? previous, Func<MainShellWindow, Task> body, bool detachStartup = true) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current == null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var old = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = service.Current;
        s.Welcomed = true;
        s.HasAcceptedAgeVerification = true;
        s.LastSeenVersion = previous ?? CoreReleaseContent.AppVersion;
        s.SeenFeatureIntros.Clear();
        s.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys.Where(k => k != "lockdown"));
        StartupLadder.ResetForTests();
        FeatureIntroPopup.ResetForTests();
        var shell = new MainShellWindow();
        if (detachStartup) shell.Opened -= shell.OnUpgradeShellOpened;
        shell.UpgradeDelay = _ => Task.CompletedTask;
        shell.Show();
        try { await body(shell); }
        finally
        {
            foreach (var w in shell.OwnedWindows.ToArray()) w.Close();
            shell.StopUpgradeIntroForPanic();
            shell.Close();
            MainShellWindow.IsStartupDialogShowing = false;
            service.SaveImmediate();
            CoreSettings.ServiceProvider = old;
            StartupLadder.ResetForTests();
            FeatureIntroPopup.ResetForTests();
            Dispatcher.UIThread.RunJobs();
        }
    });
}
