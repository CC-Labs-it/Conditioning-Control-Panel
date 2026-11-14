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
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Launcher;
using ConditioningControlPanel.Services.Startup;
using Xunit;

namespace CCP.Avalonia.Tests;

public class BrowserResetReviewTests
{
    private static string Ack => Path.Combine(CorePaths.UserData, AppUpdater.BrowserResetAcknowledged);
    private static string OldBrowser => Path.Combine(CorePaths.UserData, "browser_data");
    private static string? _oldApi;
    private static Func<bool> _oldInstalled = AppUpdater.Installed;

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
            if (!CoreSettings.Current.SeenFeatureIntros.Contains(key))
                CoreSettings.Current.SeenFeatureIntros.Add(key);
        _oldApi = Environment.GetEnvironmentVariable("CCP_UPDATE_API_URL");
        _oldInstalled = AppUpdater.Installed;
        Cleanup();
    }

    // The upgrader's own WPF browser profile, as Windows would have it.
    internal static void OldProfile()
    {
        AppUpdater.OnWindows = true;
        Directory.CreateDirectory(OldBrowser);
    }

    private static Feed UseFeed(string tag)
    {
        var feed = new Feed(tag);
        Environment.SetEnvironmentVariable("CCP_UPDATE_API_URL", "http://127.0.0.1:9/api");
        AppUpdater.Handler = feed;
        AppUpdater.Installed = () => true; // Windows CI is not an Inno install
        CoreSettings.Current.OfflineMode = false;
        return feed;
    }

    // Statics, settings and *.json are restored by TestIsolation.cs; this covers what it does not.
    internal static void Cleanup(params Window?[] windows)
    {
        AppUpdater.OpenDialog?.Close();
        AppUpdater.BrowserResetDialog?.Close();
        foreach (var window in windows)
            window?.Close();
        LauncherWindow.Instance?.Close();
        LauncherWindow.Boot = BootDecision.PanelFirst;
        MainShellWindow.IsStartupDialogShowing = false;
        AppUpdater.Handler = null;
        AppUpdater.Installed = _oldInstalled;
        Environment.SetEnvironmentVariable("CCP_UPDATE_API_URL", _oldApi);
        ReleaseFeed.ClearPendingUpdateAttempt(CorePaths.UserData);
        ReleaseFeed.ClearSkippedUpdateVersion(CorePaths.UserData);
        File.Delete(Ack);
        if (Directory.Exists(OldBrowser))
            Directory.Delete(OldBrowser, true);
    }

    private static async Task Until(Func<bool> ready)
    {
        for (int i = 0; i < 200 && !ready(); i++) await Task.Delay(50);
        Assert.True(ready(), "startup surface did not appear");
    }

    private static Task UntilChecked(Feed feed, string version, bool newer) =>
        Until(() => Volatile.Read(ref feed.Requests) > 0 && AppUpdater.Latest is { } latest
            && latest.Version == version && latest.IsNewer == newer);

    private static void CloseNotice() => AppUpdater.BrowserResetDialog!.FindControl<Button>("BtnOk")!
        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void Later() => AppUpdater.OpenDialog!.FindControl<Button>("BtnLater")!
        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public void BrowserUpgradeIsDetectedPerUserOnWindowsOnly()
    {
        var root = Directory.CreateTempSubdirectory("ccp-browser-accounts-").FullName;
        try
        {
            var upgrader = Path.Combine(root, "upgrader");
            var later = Path.Combine(root, "later-user");
            var fresh = Directory.CreateDirectory(Path.Combine(root, "fresh")).FullName;
            Directory.CreateDirectory(Path.Combine(upgrader, "browser_data"));
            Directory.CreateDirectory(Path.Combine(later, "browser_data"));
            // The upgrader already opened the new browser once.
            Directory.CreateDirectory(Path.Combine(upgrader, "browser_data_avalonia"));
            Assert.True(AppUpdater.NeedsBrowserResetNotice(upgrader, true));
            Assert.True(AppUpdater.NeedsBrowserResetNotice(later, true));
            Assert.False(AppUpdater.NeedsBrowserResetNotice(fresh, true));
            Assert.False(AppUpdater.NeedsBrowserResetNotice(upgrader, false));
            File.WriteAllText(Path.Combine(upgrader, AppUpdater.BrowserResetAcknowledged), "{}");
            Assert.False(AppUpdater.NeedsBrowserResetNotice(upgrader, true));
            Assert.True(AppUpdater.NeedsBrowserResetNotice(later, true));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task LauncherBootParksNoticeAndStartupSurfacesDoNotOverlap() => await AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        OldProfile();
        var feed = UseFeed("99.1.0");
        var shell = new MainShellWindow();
        GameWindow? game = null;
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
            await Until(() => Volatile.Read(ref feed.Requests) > 0 && AppUpdater.Latest?.IsNewer == true);
            await Task.Delay(1200);
            Assert.Null(AppUpdater.BrowserResetDialog); // the launcher holds the panel in the tray (WPF StartupQueueCore.cs:256)
            launcher.Hide();
            shell.Show();
            game = new GameWindow();
            game.Show();
            await Task.Delay(1200);
            Assert.Null(AppUpdater.BrowserResetDialog); // nor over a game
            game.Close();
            await Until(() => AppUpdater.BrowserResetDialog != null);
            Assert.Same(shell, AppUpdater.BrowserResetDialog!.Owner);
            Assert.True(MainShellWindow.IsStartupDialogShowing);
            Assert.True(StartupLadder.IsQuiet);
            var opened = false;
            StartupLadder.PresentOrInbox(new InboxItem { Key = "notice-test", Title = "News", Open = () => opened = true });
            Assert.False(opened);
            Assert.True(StartupLadder.Inbox.Contains("notice-test"));
            CoreSettings.Current.SeenFeatureIntros.Remove("lockdown");
            FeatureIntroPopup.ResetForTests();
            FeatureIntroPopup.ShowIfFirstTime("lockdown", shell);
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain("lockdown", CoreSettings.Current.SeenFeatureIntros);
            Assert.Null(AppUpdater.OpenDialog); // the update offer is last
            CloseNotice();
            await Until(() => AppUpdater.OpenDialog != null);
            Assert.Same(shell, AppUpdater.OpenDialog!.Owner);
            Later();
            await startup;
            Assert.True(File.Exists(Ack));
            Assert.False(MainShellWindow.IsStartupDialogShowing);
        }
        finally
        {
            Cleanup(announcement, game, shell);
        }
    });

    [Fact]
    public async Task NoticeUsesStartupGuardForAnnouncementsAndFeatureIntros() => await AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        AppUpdater.OnWindows = true;
        var root = Directory.CreateTempSubdirectory("ccp-browser-notice-").FullName;
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
            Cleanup(sheet, owner);
            WelcomeBackOffer.ResetForTests();
            Directory.Delete(root, true);
        }
    });

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
    public async Task PendingNoticeDoesNotDelayUpdateCheckOrFailedUpdateReport() => await AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        OldProfile();
        var feed = UseFeed("99.1.0");
        ReleaseFeed.SetPendingUpdateAttempt(CorePaths.UserData, "99.1.0");
        File.WriteAllText(ReleaseFeed.AttemptResultFilePath(CorePaths.UserData), "1");
        UpdateFailedDialog? failed = null;
        using var hook = Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => { if (w is UpdateFailedDialog f) failed = f; });
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            CoreSettings.Current.HasAcceptedAgeVerification = false;
            var startup = AppUpdater.StartupAsync(shell);
            await Until(() => failed != null);
            // The check is not held by the notice, and the failed version is already skipped: no pill.
            await UntilChecked(feed, "99.1.0", newer: false);
            CoreSettings.Current.HasAcceptedAgeVerification = true;
            await Task.Delay(1200);
            Assert.Null(AppUpdater.BrowserResetDialog); // behind the failure report (WPF App.xaml.cs:5417-5425)
            failed!.Close();
            await Until(() => AppUpdater.BrowserResetDialog != null);
            CloseNotice();
            await startup;
        }
        finally
        {
            Cleanup(failed, shell);
        }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NoticeAndUpdateOfferWaitForTutorial(bool notice) => await AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var oldProvider = CoreTutorial.IsActiveProvider;
        var feed = UseFeed("99.2.0");
        CoreSettings.Current.OfflineMode = notice;
        var shell = new MainShellWindow();
        var active = true;
        try
        {
            shell.Show();
            CoreTutorial.IsActiveProvider = () => active;
            if (notice)
                OldProfile();
            var startup = AppUpdater.StartupAsync(shell);
            if (!notice)
                await UntilChecked(feed, "99.2.0", newer: true);
            await Task.Delay(1500);
            Assert.Null(AppUpdater.BrowserResetDialog);
            Assert.Null(AppUpdater.OpenDialog);
            active = false;
            if (notice)
            {
                await Until(() => AppUpdater.BrowserResetDialog != null);
                CloseNotice();
            }
            else
            {
                await Until(() => AppUpdater.OpenDialog != null);
                Later();
            }
            await startup;
        }
        finally
        {
            active = false;
            Cleanup(shell);
            CoreTutorial.IsActiveProvider = oldProvider;
        }
    });
}
