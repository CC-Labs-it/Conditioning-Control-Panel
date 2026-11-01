using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public class BrowserResetNoticeTests
{
    [Fact]
    public async Task SilentUpgradeNoticeWaitsForAgeGateAndPersistsUntilAcknowledged()
    {
        var marker = Path.Combine(CorePaths.UserData, AppUpdater.BrowserResetMarker);
        var accepted = CoreSettings.Current.HasAcceptedAgeVerification;
        var oldApi = Environment.GetEnvironmentVariable("CCP_UPDATE_API_URL");
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            MainShellWindow? shell = null;
            try
            {
                if (Application.Current is null)
                    AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                        .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                        .SetupWithoutStarting();
                Environment.SetEnvironmentVariable("CCP_UPDATE_API_URL", null);
                File.Delete(marker);
                shell = new MainShellWindow();
                shell.Show();
                await AppUpdater.ShowBrowserResetNoticeAsync(shell, CorePaths.UserData);
                Assert.Null(AppUpdater.BrowserResetDialog);

                File.WriteAllText(marker, "pending");
                CoreSettings.Current.HasAcceptedAgeVerification = false;
                var startup = AppUpdater.StartupAsync(shell);
                await Task.Delay(1200);
                Assert.Null(AppUpdater.BrowserResetDialog);
                Assert.True(File.Exists(marker));
                CoreSettings.Current.HasAcceptedAgeVerification = true;
                for (int i = 0; i < 100 && AppUpdater.BrowserResetDialog is null; i++) await Task.Delay(50);
                var dialog = Assert.IsType<global::ConditioningControlPanel.Avalonia.Views.Dialogs.MessageDialog>(AppUpdater.BrowserResetDialog);
                var notice = Loc.Get("installer_avalonia_upgrade_notice").Split(new[] { "\n\n" }, 2, StringSplitOptions.None);
                Assert.Equal(notice[0], dialog.FindControl<TextBlock>("TxtTitle")!.Text);
                Assert.Equal(notice[1], dialog.FindControl<TextBlock>("TxtMessage")!.Text);
                dialog.Close();
                await startup;
                Assert.True(File.Exists(marker));

                var again = AppUpdater.StartupAsync(shell);
                for (int i = 0; i < 100 && AppUpdater.BrowserResetDialog is null; i++) await Task.Delay(50);
                dialog = Assert.IsType<global::ConditioningControlPanel.Avalonia.Views.Dialogs.MessageDialog>(AppUpdater.BrowserResetDialog);
                dialog.FindControl<Button>("BtnOk")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await again;
                Assert.False(File.Exists(marker));
                await AppUpdater.ShowBrowserResetNoticeAsync(shell, CorePaths.UserData);
                Assert.Null(AppUpdater.BrowserResetDialog);
            }
            finally
            {
                AppUpdater.BrowserResetDialog?.Close();
                shell?.Close();
                File.Delete(marker);
                CoreSettings.Current.HasAcceptedAgeVerification = accepted;
                Environment.SetEnvironmentVariable("CCP_UPDATE_API_URL", oldApi);
            }
        });
    }
}
