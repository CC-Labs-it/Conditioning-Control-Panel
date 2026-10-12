using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// User report "login does not work": a provider sign-in left waiting (browser closed, dialog cancelled) kept the
/// callback port and the provider's IsVerifying latch for its 5-minute timeout, so every retry from any door
/// (Home Login, Settings · Account, launcher Sign in) bounced straight back to the provider list. Closing the
/// dialog must end the flow so the next click starts a fresh one.
/// </summary>
public sealed class LoginRetryTests
{
    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    /// <summary>Bounded by time, not turns (P73): the cancellation lands on a pool thread.</summary>
    private static async Task Until(Func<bool> done, string what)
    {
        var sw = Stopwatch.StartNew();
        while (!done() && sw.Elapsed < TimeSpan.FromSeconds(10))
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Yield();
        }
        Assert.True(done(), what);
    }

    [Fact]
    public Task ClosingTheDialogEndsTheProviderFlowSoTheNextClickStartsAgain() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var (oldGet, oldSet, oldShell) = (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider, ExternalOpener.Shell);
        // Seed() below rewires every CoreAccount/CoreEntitlement seam; all of them go back (P02).
        var oldAccount = (CoreAccount.IsLoggedInProvider, CoreAccount.DisplayNameProvider, CoreAccount.IsWhitelistedProvider,
            CoreAccount.HasPremiumAccessProvider, CoreAccount.HasLabAccessProvider, CoreAccount.ChangeDisplayNameProvider,
            CoreAccount.DeleteAccountProvider, CoreEntitlement.HasPremiumProvider, CoreEntitlement.HasLabProvider);
        var secrets = new Dictionary<string, string?>(); // memory only: never the keyring
        (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider) = (n => secrets.GetValueOrDefault(n), (n, v) => secrets[n] = v);
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        (s.UnifiedId, s.OfflineMode) = (null, false);
        ExternalOpener.Shell = _ => true; // never a real browser
        Assert.True(AccountSeed.Seed(p => new ProviderSubscription(p, () => s), p => new DiscordAccount(() => p, () => s)));
        var discord = AccountSeed.Discord!;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var login = shell.Named<SettingsTabView>("SettingsTab")!.FindControl<Button>("BtnUnifiedLogin")!;

            for (var attempt = 1; attempt <= 2; attempt++)
            {
                Click(login);
                Dispatcher.UIThread.RunJobs();
                var dialog = shell.OwnedWindows.OfType<LoginDialog>().Single();
                Click(dialog.FindControl<Button>("BtnLoginDiscord")!);
                // Waiting on the browser: the listener is bound and the flow holds the latch.
                await Until(() => discord.IsVerifying, $"attempt {attempt}: the Discord flow started");
                Assert.Empty(dialog.OwnedWindows.OfType<MessageDialog>()); // no "could not listen on port" error

                Click(dialog.FindControl<Button>("BtnCancel")!);
                await Until(() => !discord.IsVerifying, $"attempt {attempt}: closing the dialog ended the flow");
            }
        }
        finally
        {
            foreach (var w in shell.OwnedWindows.ToList()) w.Close();
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            CoreSettings.ServiceProvider = null;
            (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider, ExternalOpener.Shell) = (oldGet, oldSet, oldShell);
            (CoreAccount.IsLoggedInProvider, CoreAccount.DisplayNameProvider, CoreAccount.IsWhitelistedProvider,
                CoreAccount.HasPremiumAccessProvider, CoreAccount.HasLabAccessProvider, CoreAccount.ChangeDisplayNameProvider,
                CoreAccount.DeleteAccountProvider, CoreEntitlement.HasPremiumProvider, CoreEntitlement.HasLabProvider) = oldAccount;
        }
    });

    /// <summary>The title-bar "Sign in" chip opens Settings at Account (WPF MainWindow.UiUpdates.cs). On the first
    /// open of a launch the page had never been measured, the retry ran before the layout pass, and the user landed
    /// on General with no login in sight.</summary>
    [Fact]
    public Task TheAccountChipLandsOnAccountOnTheFirstOpen() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            Click(shell.Named<Button>("BtnAccountChip")!);
            var page = shell.AppSettingsPage!;
            var scroll = page.FindControl<ScrollViewer>("SectionScroll")!;
            var account = page.FindControl<Control>("SectionAccount")!;
            double Top() => account.TranslatePoint(new Point(0, 0), scroll)?.Y ?? double.NaN;
            await Until(() => scroll.Offset.Y > 0 && Math.Abs(Top()) < 24, "Account sits at the top of the page");
        }
        finally
        {
            shell.Close();
            Dispatcher.UIThread.RunJobs();
        }
    });
}
