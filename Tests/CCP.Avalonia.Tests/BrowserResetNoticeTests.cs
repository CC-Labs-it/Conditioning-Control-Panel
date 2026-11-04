using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

public class BrowserResetNoticeTests
{
    [Fact]
    public async Task SilentUpgradeNoticeWaitsForAgeGateAndPersistsUntilAcknowledged() => await AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var ack = Path.Combine(CorePaths.UserData, AppUpdater.BrowserResetAcknowledged);
        MainShellWindow? shell = null;
        try
        {
            BrowserResetReviewTests.Setup();
            CoreSettings.Current.OfflineMode = true;
            shell = new MainShellWindow();
            shell.Show();
            await AppUpdater.ShowBrowserResetNoticeAsync(shell, CorePaths.UserData);
            Assert.Null(AppUpdater.BrowserResetDialog); // no old WPF profile

            BrowserResetReviewTests.OldProfile();
            CoreSettings.Current.HasAcceptedAgeVerification = false;
            var startup = AppUpdater.StartupAsync(shell);
            await Task.Delay(1200);
            Assert.Null(AppUpdater.BrowserResetDialog);
            CoreSettings.Current.HasAcceptedAgeVerification = true;
            for (int i = 0; i < 100 && AppUpdater.BrowserResetDialog is null; i++)
                await Task.Delay(50);
            var dialog = Assert.IsType<MessageDialog>(AppUpdater.BrowserResetDialog);
            var notice = Loc.Get("installer_avalonia_upgrade_notice").Split(new[] { "\n\n" }, 2, StringSplitOptions.None);
            Assert.Equal(notice[0], dialog.FindControl<TextBlock>("TxtTitle")!.Text);
            Assert.Equal(notice[1], dialog.FindControl<TextBlock>("TxtMessage")!.Text);
            dialog.Close();
            await startup;
            Assert.False(File.Exists(ack)); // closing is not OK, so the notice is still owed

            var again = AppUpdater.StartupAsync(shell);
            for (int i = 0; i < 100 && AppUpdater.BrowserResetDialog is null; i++)
                await Task.Delay(50);
            dialog = Assert.IsType<MessageDialog>(AppUpdater.BrowserResetDialog);
            dialog.FindControl<Button>("BtnOk")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await again;
            Assert.True(File.Exists(ack));
            await AppUpdater.ShowBrowserResetNoticeAsync(shell, CorePaths.UserData);
            Assert.Null(AppUpdater.BrowserResetDialog);
        }
        finally
        {
            BrowserResetReviewTests.Cleanup(shell);
        }
    });
}
