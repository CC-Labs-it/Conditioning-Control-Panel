using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Restack b161 (#4037, hand-ported onto the trunk's StartupLadder modal queue): a silent WPF upgrade
/// skips the installer's notice page, so the app shows the browser login reset once, between the
/// failed-update report (10) and the update offer (80), until the player presses OK.
/// </summary>
public class BrowserResetNoticeTests
{
    private static string Ack => Path.Combine(CorePaths.UserData, AppUpdater.BrowserResetAcknowledged);
    private static string OldBrowser => Path.Combine(CorePaths.UserData, "browser_data");

    private static MainShellWindow Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        StartupLadder.ResetForTests();
        CoreSettings.Current.HasAcceptedAgeVerification = true;
        CoreSettings.Current.OfflineMode = true;   // no feed: only the notice is under test
        Cleanup();
        var shell = new MainShellWindow();
        StartupLadder.ModalWindowReady = () => shell.IsVisible;
        shell.Show();
        return shell;
    }

    // The upgrader's own WPF browser profile, as Windows would have it.
    private static void OldProfile()
    {
        AppUpdater.OnWindows = true;
        Directory.CreateDirectory(OldBrowser);
    }

    private static void Cleanup(Window? shell = null)
    {
        AppUpdater.BrowserResetDialog?.Close();
        shell?.Close();
        StartupLadder.ResetForTests();
        MainShellWindow.IsStartupDialogShowing = false;
        File.Delete(Ack);
        if (Directory.Exists(OldBrowser))
            Directory.Delete(OldBrowser, true);
    }

    private static async Task Step(TimeSpan by)
    {
        ((StartupLadder.TestClock)StartupLadder.Time).Advance(by);
        for (var i = 0; i < 5; i++) await Task.Yield();
    }

    // Steps the ladder clock while real time runs the age-gate poll and the queue post.
    private static async Task<MessageDialog?> NoticeAfterSteps(int steps = 60)
    {
        for (var i = 0; i < steps && AppUpdater.BrowserResetDialog is null; i++)
        {
            await Step(StartupLadder.ModalSettle);
            await Task.Delay(50);
        }
        return AppUpdater.BrowserResetDialog;
    }

    private static void PressOk(MessageDialog dialog) =>
        dialog.FindControl<Button>("BtnOk")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

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
    public async Task SilentUpgradeNoticeWaitsForAgeGateAndPersistsUntilAcknowledged() => await AvaloniaTestDispatcher.RunAsync(async () =>
    {
        MainShellWindow? shell = null;
        try
        {
            shell = Setup();
            await AppUpdater.ShowBrowserResetNoticeAsync(shell, CorePaths.UserData);
            Assert.Null(AppUpdater.BrowserResetDialog); // no old WPF profile

            OldProfile();
            CoreSettings.Current.HasAcceptedAgeVerification = false;
            await AppUpdater.StartupAsync(shell);
            Assert.Null(await NoticeAfterSteps(10));
            CoreSettings.Current.HasAcceptedAgeVerification = true;
            var dialog = Assert.IsType<MessageDialog>(await NoticeAfterSteps());
            var notice = Loc.Get("installer_avalonia_upgrade_notice").Split(new[] { "\n\n" }, 2, StringSplitOptions.None);
            Assert.Equal(notice[0], dialog.FindControl<TextBlock>("TxtTitle")!.Text);
            Assert.Equal(notice[1], dialog.FindControl<TextBlock>("TxtMessage")!.Text);
            Assert.True(MainShellWindow.IsStartupDialogShowing); // the ladder's startup guard covers it
            dialog.Close();
            for (var i = 0; i < 20 && AppUpdater.BrowserResetDialog != null; i++) await Task.Delay(25);
            Assert.False(File.Exists(Ack)); // closing is not OK, so the notice is still owed

            await AppUpdater.StartupAsync(shell);
            dialog = Assert.IsType<MessageDialog>(await NoticeAfterSteps());
            PressOk(dialog);
            for (var i = 0; i < 40 && !File.Exists(Ack); i++) await Task.Delay(25);
            Assert.True(File.Exists(Ack));
            await AppUpdater.ShowBrowserResetNoticeAsync(shell, CorePaths.UserData);
            Assert.Null(AppUpdater.BrowserResetDialog);
        }
        finally
        {
            Cleanup(shell);
        }
    });

    [Fact]
    public async Task NoticeWaitsBehindEarlierModalsAndTheLauncherHoldAndGoesBeforeTheUpdateOffer() => await AvaloniaTestDispatcher.RunAsync(async () =>
    {
        MainShellWindow? shell = null;
        try
        {
            shell = Setup();
            OldProfile();
            var report = new TaskCompletionSource();
            var offerShown = false;
            StartupLadder.EnqueueModal("failed-update-report", 10, () => report.Task);
            StartupLadder.EnqueueModal("update-available", 80, () => { offerShown = true; return Task.CompletedTask; });
            StartupLadder.Hold();
            await AppUpdater.StartupAsync(shell);
            Assert.Null(await NoticeAfterSteps(10)); // the launcher hold parks the whole ladder
            StartupLadder.Release();
            await NoticeAfterSteps(10);
            Assert.Null(AppUpdater.BrowserResetDialog); // the failed-update report is still up
            report.SetResult();
            var dialog = Assert.IsType<MessageDialog>(await NoticeAfterSteps());
            Assert.False(offerShown);
            PressOk(dialog);
            for (var i = 0; i < 40 && !offerShown; i++) { await Step(StartupLadder.ModalSettle); await Task.Delay(25); }
            Assert.True(offerShown);
            Assert.True(File.Exists(Ack));
        }
        finally
        {
            Cleanup(shell);
        }
    });
}
