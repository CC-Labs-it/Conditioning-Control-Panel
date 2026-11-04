using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Launcher;
using ConditioningControlPanel.Services.Startup;
using Xunit;

namespace CCP.Avalonia.Tests;

public class BrowserResetReviewTests
{
    [Fact]
    public async Task ManualLaterDuringPanelWaitSuppressesStartupOffer() => await AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup(); var oldApi = Environment.GetEnvironmentVariable("CCP_UPDATE_API_URL"); var feed = new Feed("99.1.0"); var shell = new MainShellWindow(); var game = new global::ConditioningControlPanel.Avalonia.Views.Games.GameWindow();
        try { Environment.SetEnvironmentVariable("CCP_UPDATE_API_URL", "http://127.0.0.1:9/api"); AppUpdater.Handler = feed; CoreSettings.Current.OfflineMode = false; ReleaseFeed.ClearSkippedUpdateVersion(CorePaths.UserData); shell.Show(); game.Show(); var startup = AppUpdater.StartupAsync(shell); await Until(() => Volatile.Read(ref feed.Requests) > 0 && AppUpdater.Latest is { Version: "99.1.0", IsNewer: true }); await Task.Delay(1000); Assert.Null(AppUpdater.OpenDialog); var manual = AppUpdater.ManualCheckAsync(game); await Until(() => AppUpdater.OpenDialog != null); AppUpdater.OpenDialog!.FindControl<Button>("BtnLater")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await manual; Assert.Equal("99.1.0", ReleaseFeed.GetSkippedUpdateVersion(CorePaths.UserData)); game.Close(); Assert.Same(startup, await Task.WhenAny(startup, Task.Delay(5000))); await startup; Assert.Null(AppUpdater.OpenDialog); Assert.Equal("99.1.0", ReleaseFeed.GetSkippedUpdateVersion(CorePaths.UserData)); }
        finally { AppUpdater.OpenDialog?.Close(); game.Close(); shell.Close(); ReleaseFeed.ClearSkippedUpdateVersion(CorePaths.UserData); AppUpdater.Handler = null; Environment.SetEnvironmentVariable("CCP_UPDATE_API_URL", oldApi); }
    });
    [Fact] public async Task ManualLaterSurvivesExitWhileGameOpenAndRestart() { await AvaloniaTestDispatcher.RunAsync(async () => { Setup(); var oldApi = Environment.GetEnvironmentVariable("CCP_UPDATE_API_URL"); var feed = new Feed("99.1.0"); var shell = new MainShellWindow(); var shell2 = new MainShellWindow(); var game = new global::ConditioningControlPanel.Avalonia.Views.Games.GameWindow(); try { Environment.SetEnvironmentVariable("CCP_UPDATE_API_URL", "http://127.0.0.1:9/api"); AppUpdater.Handler = feed; CoreSettings.Current.OfflineMode = false; ReleaseFeed.SetPendingUpdateAttempt(CorePaths.UserData, CoreReleaseContent.AppVersion); File.WriteAllText(ReleaseFeed.AttemptResultFilePath(CorePaths.UserData), "0"); shell.Show(); game.Show(); var startup = AppUpdater.StartupAsync(shell); await Until(() => Volatile.Read(ref feed.Requests) > 0 && AppUpdater.Latest is { Version: "99.1.0", IsNewer: true }); Assert.False(File.Exists(ReleaseFeed.AttemptFilePath(CorePaths.UserData))); var manual = AppUpdater.ManualCheckAsync(game); await Until(() => AppUpdater.OpenDialog != null); AppUpdater.OpenDialog!.FindControl<Button>("BtnLater")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await manual; Assert.Equal("99.1.0", ReleaseFeed.GetSkippedUpdateVersion(CorePaths.UserData)); shell.Close(); await startup; shell2.Show(); var restart = AppUpdater.StartupAsync(shell2); await Until(() => Volatile.Read(ref feed.Requests) > 1); game.Close(); await restart; Assert.Equal("99.1.0", ReleaseFeed.GetSkippedUpdateVersion(CorePaths.UserData)); Assert.Null(AppUpdater.OpenDialog); } finally { AppUpdater.OpenDialog?.Close(); game.Close(); shell.Close(); shell2.Close(); ReleaseFeed.ClearPendingUpdateAttempt(CorePaths.UserData); ReleaseFeed.ClearSkippedUpdateVersion(CorePaths.UserData); AppUpdater.Handler = null; Environment.SetEnvironmentVariable("CCP_UPDATE_API_URL", oldApi); } }); }
    internal static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        StartupLadder.ResetForTests();
        CoreSettings.Current.Welcomed = true;
        CoreSettings.Current.HasAcceptedAgeVerification = true;
        CoreSettings.Current.WhatMovedCardShown = 1;
        foreach (var key in FeatureIntros.All.Keys)
            if (!CoreSettings.Current.SeenFeatureIntros.Contains(key)) CoreSettings.Current.SeenFeatureIntros.Add(key);
    }

    private static async Task Until(Func<bool> ready)
    {
        for (int i = 0; i < 200 && !ready(); i++) await Task.Delay(50);
        Assert.True(ready(), "startup surface did not appear");
    }

    private static void CloseNotice() => AppUpdater.BrowserResetDialog!.FindControl<Button>("BtnOk")!
        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public void BrowserUpgradeIsDetectedForEachUserEvenAfterAnotherAccountInstalled()
    {
        var root = Directory.CreateTempSubdirectory("ccp-browser-accounts-").FullName;
        try
        {
            var admin = Path.Combine(root, "admin");
            var upgrader = Path.Combine(root, "upgrader");
            var later = Path.Combine(root, "later-user");
            var fresh = Path.Combine(root, "fresh");
            foreach (var dir in new[] { admin, upgrader, later }) Directory.CreateDirectory(Path.Combine(dir, "browser_data"));
            Directory.CreateDirectory(fresh);
            // The installer ran as admin; the upgrader already opened the new browser once.
            File.WriteAllText(Path.Combine(admin, AppUpdater.BrowserResetMarker), "pending");
            Directory.CreateDirectory(Path.Combine(upgrader, "browser_data_avalonia"));
            Assert.True(AppUpdater.NeedsBrowserResetNotice(admin, true));
            Assert.True(AppUpdater.NeedsBrowserResetNotice(upgrader, true));
            Assert.True(AppUpdater.NeedsBrowserResetNotice(later, true));
            Assert.False(AppUpdater.NeedsBrowserResetNotice(fresh, true));
            Assert.False(AppUpdater.NeedsBrowserResetNotice(upgrader, false));
            File.WriteAllText(Path.Combine(admin, AppUpdater.BrowserResetAcknowledged), "{}");
            Assert.False(AppUpdater.NeedsBrowserResetNotice(admin, true)); // stale installer marker ignored
            Assert.True(AppUpdater.NeedsBrowserResetNotice(upgrader, true));
            File.WriteAllText(Path.Combine(upgrader, AppUpdater.BrowserResetAcknowledged), "{}");
            Assert.False(AppUpdater.NeedsBrowserResetNotice(upgrader, true));
            Assert.True(AppUpdater.NeedsBrowserResetNotice(later, true));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task LauncherBootOwnsNoticeAndStartupSurfacesDoNotOverlap()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            Setup();
            var marker = Path.Combine(CorePaths.UserData, AppUpdater.BrowserResetMarker);
            var ack = Path.Combine(CorePaths.UserData, AppUpdater.BrowserResetAcknowledged);
            File.Delete(ack);
            File.WriteAllText(marker, "pending");
            CoreSettings.Current.OfflineMode = false;
            var oldApi = Environment.GetEnvironmentVariable("CCP_UPDATE_API_URL");
            Environment.SetEnvironmentVariable("CCP_UPDATE_API_URL", "http://127.0.0.1:9/api");
            var feed = new Feed("99.1.0");
            AppUpdater.Handler = feed;
            ReleaseFeed.ClearSkippedUpdateVersion(CorePaths.UserData);
            var shell = new MainShellWindow();
            global::ConditioningControlPanel.Avalonia.Views.Games.GameWindow? game = null;
            AnnouncementPopup? announcement = null;
            try
            {
                shell.Show();
                LauncherWindow.Boot = BootDecision.LauncherFirst;
                LauncherWindow.RouteBoot(shell);
                Assert.False(shell.IsVisible);
                var launcher = Assert.IsType<LauncherWindow>(LauncherWindow.Instance);
                Assert.True(launcher.IsVisible);

                announcement = new AnnouncementPopup("test", "News", "Message", null, null, null);
                announcement.Show();
                var startup = AppUpdater.StartupAsync(shell);
                await Task.Delay(1300);
                Assert.Null(AppUpdater.BrowserResetDialog); // a passive surface is already up
                announcement.Close();
                announcement = null;
                await Until(() => AppUpdater.BrowserResetDialog != null);
                Assert.Same(launcher, AppUpdater.BrowserResetDialog!.Owner);
                Assert.True(MainShellWindow.IsStartupDialogShowing);
                Assert.True(StartupLadder.IsQuiet);
                var opened = false;
                StartupLadder.PresentOrInbox(new InboxItem { Key = "notice-test", Title = "News", Open = () => opened = true });
                Assert.False(opened);
                Assert.True(StartupLadder.Inbox.Contains("notice-test"));
                CoreSettings.Current.SeenFeatureIntros.Remove("lockdown");
                FeatureIntroPopup.ResetForTests();
                FeatureIntroPopup.ShowIfFirstTime("lockdown", launcher);
                Dispatcher.UIThread.RunJobs();
                Assert.DoesNotContain("lockdown", CoreSettings.Current.SeenFeatureIntros);
                await Until(() => Volatile.Read(ref feed.Requests) > 0 && AppUpdater.Latest?.IsNewer == true);
                CloseNotice();
                await Task.Delay(1200);
                Assert.Null(AppUpdater.OpenDialog); // updates wait while the launcher owns the screen
                launcher.Hide();
                shell.Show();
                game = new global::ConditioningControlPanel.Avalonia.Views.Games.GameWindow();
                game.Show();
                await Task.Delay(1200);
                Assert.Null(AppUpdater.OpenDialog); // and while a game is open, even with the panel visible
                game.Close();
                game = null;
                await Until(() => AppUpdater.OpenDialog != null);
                Assert.Same(shell, AppUpdater.OpenDialog!.Owner);
                AppUpdater.OpenDialog.FindControl<Button>("BtnLater")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await startup;
                Assert.True(File.Exists(ack));
                Assert.False(File.Exists(marker));
                Assert.False(MainShellWindow.IsStartupDialogShowing);
            }
            finally
            {
                announcement?.Close();
                game?.Close();
                AppUpdater.OpenDialog?.Close();
                AppUpdater.BrowserResetDialog?.Close();
                shell.Close();
                AppUpdater.Handler = null;
                Environment.SetEnvironmentVariable("CCP_UPDATE_API_URL", oldApi);
                ReleaseFeed.ClearSkippedUpdateVersion(CorePaths.UserData);
                File.Delete(marker); File.Delete(ack);
            }
        });
    }

    [Fact]
    public async Task NoticeUsesStartupGuardForAnnouncementsAndFeatureIntros()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            Setup();
            var root = Directory.CreateTempSubdirectory("ccp-browser-notice-").FullName;
            File.WriteAllText(Path.Combine(root, AppUpdater.BrowserResetMarker), "pending");
            Directory.CreateDirectory(Path.Combine(root, "browser_data"));
            var other = Directory.CreateDirectory(Path.Combine(root, "other-user")).FullName;
            Directory.CreateDirectory(Path.Combine(other, "browser_data"));
            var owner = new Window();
            WelcomeBackOffer.ResetForTests();
            WelcomeBackSheet? sheet = null;
            using var hook = Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => { if (w is WelcomeBackSheet s) sheet = s; });
            try
            {
                owner.Show();
                var showing = AppUpdater.ShowBrowserResetNoticeAsync(owner, root);
                Assert.NotNull(AppUpdater.BrowserResetDialog);
                Assert.True(MainShellWindow.IsStartupDialogShowing);
                var opened = false;
                StartupLadder.PresentOrInbox(new InboxItem { Key = "news", Title = "News", Open = () => opened = true });
                Assert.False(opened);
                Assert.True(StartupLadder.Inbox.Contains("news"));
                CoreSettings.Current.SeenFeatureIntros.Remove("lockdown");
                FeatureIntroPopup.ResetForTests();
                FeatureIntroPopup.ShowIfFirstTime("lockdown", owner);
                Dispatcher.UIThread.RunJobs();
                Assert.DoesNotContain("lockdown", CoreSettings.Current.SeenFeatureIntros);
                var welcome = WelcomeBackOffer.Present(new WelcomeBackSheetContent());
                await Task.Delay(700);
                Assert.Null(sheet);
                CloseNotice();
                await showing;
                await Until(() => sheet != null);
                sheet!.Close();
                await welcome;
                Assert.True(File.Exists(Path.Combine(root, AppUpdater.BrowserResetAcknowledged)));
                Assert.False(AppUpdater.NeedsBrowserResetNotice(root, true));
                Assert.True(AppUpdater.NeedsBrowserResetNotice(other, true));
                Assert.False(MainShellWindow.IsStartupDialogShowing);
            }
            finally
            {
                sheet?.Close();
                AppUpdater.BrowserResetDialog?.Close();
                owner.Close();
                WelcomeBackOffer.ResetForTests();
                Directory.Delete(root, true);
            }
        });
    }

    [Fact]
    public async Task FailedUpdateIsOwnerlessInTrayAndRetainedIfExitPrecedesDisplay()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            Setup();
            CoreSettings.Current.OfflineMode = true;
            var shell = new MainShellWindow();
            UpdateFailedDialog? failed = null;
            using var hook = Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => { if (w is UpdateFailedDialog f) failed = f; });
            global::ConditioningControlPanel.Avalonia.Views.Games.GameWindow? game = null;
            try
            {
                shell.Show();
                LauncherWindow.Boot = BootDecision.LauncherFirst;
                LauncherWindow.RouteBoot(shell); // panel tucked in the tray, launcher up
                game = new global::ConditioningControlPanel.Avalonia.Views.Games.GameWindow();
                game.Show();
                ReleaseFeed.SetPendingUpdateAttempt(CorePaths.UserData, "99.1.0");
                File.WriteAllText(ReleaseFeed.AttemptResultFilePath(CorePaths.UserData), "1");
                var startup = AppUpdater.StartupAsync(shell);
                await Task.Delay(1300);
                Assert.Null(failed); // parked behind the launcher and the game
                LauncherWindow.Instance!.Hide();
                await Task.Delay(1200);
                Assert.Null(failed); // and behind the game alone
                Assert.True(File.Exists(ReleaseFeed.AttemptFilePath(CorePaths.UserData)));
                game.Close(); game = null;
                await Until(() => failed != null);
                Assert.Null(failed!.Owner);
                Assert.False(File.Exists(ReleaseFeed.AttemptFilePath(CorePaths.UserData)));
                failed.Close(); await startup; failed = null;
                ReleaseFeed.SetPendingUpdateAttempt(CorePaths.UserData, "99.2.0");
                MainShellWindow.IsStartupDialogShowing = true;
                startup = AppUpdater.StartupAsync(shell);
                await Task.Delay(900);
                shell.Close();
                MainShellWindow.IsStartupDialogShowing = false;
                await startup;
                Assert.True(File.Exists(ReleaseFeed.AttemptFilePath(CorePaths.UserData)));
            }
            finally
            {
                MainShellWindow.IsStartupDialogShowing = false;
                LauncherWindow.Boot = BootDecision.PanelFirst;
                failed?.Close(); game?.Close(); LauncherWindow.Instance?.Close(); shell.Close();
                ReleaseFeed.ClearPendingUpdateAttempt(CorePaths.UserData);
            }
        });
    }

    private sealed class Feed(string tag = "0.0.0") : HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Requests);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent($"{{\"tag_name\":\"v{tag}\"}}") });
        }
    }

    [Fact]
    public async Task PendingNoticeDoesNotDelayUpdateCheckOrFailedUpdateReport()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            Setup();
            var marker = Path.Combine(CorePaths.UserData, AppUpdater.BrowserResetMarker);
            var ack = Path.Combine(CorePaths.UserData, AppUpdater.BrowserResetAcknowledged);
            var oldApi = Environment.GetEnvironmentVariable("CCP_UPDATE_API_URL");
            var feed = new Feed("99.1.0");
            File.Delete(ack);
            File.WriteAllText(marker, "pending");
            Environment.SetEnvironmentVariable("CCP_UPDATE_API_URL", "http://127.0.0.1:9/api");
            AppUpdater.Handler = feed;
            CoreSettings.Current.OfflineMode = false;
            ReleaseFeed.SetPendingUpdateAttempt(CorePaths.UserData, "99.1.0");
            File.WriteAllText(ReleaseFeed.AttemptResultFilePath(CorePaths.UserData), "1");
            UpdateFailedDialog? failed = null;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) =>
            { if (w is UpdateFailedDialog f) failed = f; });
            var shell = new MainShellWindow();
            try
            {
                shell.Show();
                CoreSettings.Current.HasAcceptedAgeVerification = false;
                MainShellWindow.IsStartupDialogShowing = true; // another startup modal delays the report
                var startup = AppUpdater.StartupAsync(shell);
                // The check finishes first, yet the failed version is already skipped: no pill.
                await Until(() => Volatile.Read(ref feed.Requests) > 0 && AppUpdater.Latest is { Version: "99.1.0", IsNewer: false });
                Assert.Null(failed);
                MainShellWindow.IsStartupDialogShowing = false;
                await Until(() => failed != null);
                Assert.Null(AppUpdater.BrowserResetDialog); // still behind the age gate and failure report
                Assert.False(File.Exists(ReleaseFeed.AttemptFilePath(CorePaths.UserData)));
                failed!.Close();
                CoreSettings.Current.HasAcceptedAgeVerification = true;
                await Until(() => AppUpdater.BrowserResetDialog != null);
                CloseNotice();
                await startup;
            }
            finally
            {
                MainShellWindow.IsStartupDialogShowing = false; failed?.Close();
                AppUpdater.BrowserResetDialog?.Close();
                shell.Close();
                File.Delete(marker); File.Delete(ack);
                ReleaseFeed.ClearPendingUpdateAttempt(CorePaths.UserData);
                AppUpdater.Handler = null;
                Environment.SetEnvironmentVariable("CCP_UPDATE_API_URL", oldApi);
            }
        });
    }
}
