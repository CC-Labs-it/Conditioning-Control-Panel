using System;
using System.Linq;
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
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public class MergedSignInOfferTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task RecoveryOfferAsksBeforeOpeningLogin(bool inbox, bool confirm) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.Welcomed = true;
        settings.Current.HasAcceptedAgeVerification = true;
        StartupLadder.ResetForTests();
        MainShellWindow? shell = null;
        try
        {
            Assert.True(AccountSeed.Seed(p => new ProviderSubscription(p, () => settings.Current)));
            shell = new MainShellWindow();
            shell.Show();
            StartupLadder.ResetForTests();
            AccountSeed.RecoveryShell = () => shell;
            if (inbox)
            {
                StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(1));
                MergedAccountRecovery.Finished!("u_canon123456", false);
                Dispatcher.UIThread.RunJobs();
                var row = Assert.Single(StartupLadder.Inbox.Items, i => i.Key == "merged-account-signin:u_canon123456");
                Assert.Empty(shell.OwnedWindows.OfType<MessageDialog>());
                StartupLadder.Inbox.Open(row);
            }
            else _ = shell.OpenLoginAfterMergedSwap();
            await Until(() => shell.OwnedWindows.OfType<MessageDialog>().Any());
            var prompt = Assert.Single(shell.OwnedWindows.OfType<MessageDialog>());
            Assert.Equal(Loc.Get("account_merged_signin_title"), prompt.Title);
            Assert.Equal(Loc.Get("account_merged_signin_body"), prompt.FindControl<TextBlock>("TxtMessage")!.Text);
            Assert.True(prompt.FindControl<Button>("BtnCancel")!.IsVisible);
            Assert.Empty(shell.OwnedWindows.OfType<LoginDialog>());
            prompt.FindControl<Button>(confirm ? "BtnOk" : "BtnCancel")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => !shell.OwnedWindows.Contains(prompt));
            Dispatcher.UIThread.RunJobs();
            if (confirm) await Until(() => shell.OwnedWindows.OfType<LoginDialog>().Any());
            Assert.Equal(confirm, shell.OwnedWindows.OfType<LoginDialog>().Any());
        }
        finally
        {
            foreach (var window in shell?.OwnedWindows.ToArray() ?? Array.Empty<Window>()) window.Close();
            shell?.RequestExit();
            StartupLadder.ResetForTests();
            Dispatcher.UIThread.RunJobs();
            settings.SaveImmediate();
        }
    });

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
        Assert.True(condition(), "timed out");
    }
}
