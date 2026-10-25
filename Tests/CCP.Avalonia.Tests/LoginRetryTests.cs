using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// User report "login does not work" (2026-10-12), in a sandboxed instance where every remote host is refused:
/// every provider button spun forever. The provider flow makes no request until the browser comes back, so a dead
/// network meant the full 5-minute wait; and an abandoned flow kept the callback port and the provider's
/// IsVerifying latch for that long, so every retry from any door bounced straight back to the provider list.
/// </summary>
public sealed class LoginRetryTests
{
    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    /// <summary>Bounded by time, not turns (P73): the cancellation and the refused probe land on pool threads.</summary>
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

    /// <summary>A shown shell with memory-only secrets, seeded providers and no real browser; every seam restored (P02).</summary>
    private static Task WithShell(Func<MainShellWindow, AppSettings, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var (oldGet, oldSet, oldShell, oldProbe, oldTell) = (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider,
            ExternalOpener.Shell, LoginDialog.ProxyReachable, AccountSettingsSection.Tell);
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
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            await body(shell, s);
        }
        finally
        {
            foreach (var w in shell.OwnedWindows.ToList()) w.Close();
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            CoreSettings.ServiceProvider = null;
            (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider, ExternalOpener.Shell, LoginDialog.ProxyReachable, AccountSettingsSection.Tell) =
                (oldGet, oldSet, oldShell, oldProbe, oldTell);
            (CoreAccount.IsLoggedInProvider, CoreAccount.DisplayNameProvider, CoreAccount.IsWhitelistedProvider,
                CoreAccount.HasPremiumAccessProvider, CoreAccount.HasLabAccessProvider, CoreAccount.ChangeDisplayNameProvider,
                CoreAccount.DeleteAccountProvider, CoreEntitlement.HasPremiumProvider, CoreEntitlement.HasLabProvider) = oldAccount;
        }
    });

    private static LoginDialog OpenDialog(MainShellWindow shell)
    {
        Click(shell.Named<SettingsTabView>("SettingsTab")!.FindControl<Button>("BtnUnifiedLogin")!);
        Dispatcher.UIThread.RunJobs();
        return shell.OwnedWindows.OfType<LoginDialog>().Single();
    }

    private static bool Verifying(string provider) => provider switch
    {
        "discord" => AccountSeed.Discord!.IsVerifying,
        "substar" => AccountSeed.SubscribeStar!.IsVerifying,
        _ => AccountSeed.Patreon!.IsVerifying,
    };

    [Fact]
    public Task ClosingTheDialogEndsTheProviderFlowSoTheNextClickStartsAgain() => WithShell(async (shell, _) =>
    {
        LoginDialog.ProxyReachable = _ => Task.FromResult(true);
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var dialog = OpenDialog(shell);
            Click(dialog.FindControl<Button>("BtnLoginDiscord")!);
            // Waiting on the browser: the listener is bound and the flow holds the latch.
            await Until(() => AccountSeed.Discord!.IsVerifying, $"attempt {attempt}: the Discord flow started");
            Assert.Empty(dialog.OwnedWindows.OfType<MessageDialog>()); // no "could not listen on port" error

            Click(dialog.FindControl<Button>("BtnCancel")!);
            await Until(() => !AccountSeed.Discord!.IsVerifying, $"attempt {attempt}: closing the dialog ended the flow");
        }
    });

    /// <summary>The user's exact case: a sandbox refuses the proxy, as no network does. Each provider ends the attempt
    /// at once with WPF's failure copy (LoginDialog.xaml.cs catch: login_failed_please_try_again) and never starts the
    /// browser flow. The real probe runs: this host is a CCP_USERDATA_DIR sandbox.</summary>
    [Theory]
    [InlineData("BtnLoginDiscord", "discord")]
    [InlineData("BtnLoginPatreon", "patreon")]
    [InlineData("BtnLoginSubscribeStar", "substar")]
    public Task AnUnreachableServerEndsTheSpinnerWithWpfsError(string button, string provider) => WithShell(async (shell, _) =>
    {
        Assert.True(SandboxNet.Active);
        var dialog = OpenDialog(shell);
        Click(dialog.FindControl<Button>(button)!);
        await Until(() => dialog.OwnedWindows.OfType<MessageDialog>().Any(), $"{provider}: an error dialog, not a spinner");
        var box = dialog.OwnedWindows.OfType<MessageDialog>().Single();
        Assert.Equal(Loc.Get("login_failed_please_try_again"), box.FindControl<TextBlock>("TxtMessage")!.Text);
        Assert.False(Verifying(provider)); // the 5-minute browser wait never started
        box.Close();
        await Until(() => dialog.FindControl<StackPanel>("ProviderPanel")!.IsVisible, "back to the provider list");
    });

    /// <summary>Settings · Account "Link Discord" (signed in): same dead network, the link-failed copy at once.</summary>
    [Fact]
    public Task AnUnreachableServerEndsTheLinkAttempt() => WithShell(async (shell, s) =>
    {
        s.UnifiedId = "u_link";
        string? told = null;
        AccountSettingsSection.Tell = (_, _, body) => { told = body; return Task.CompletedTask; };
        shell.OpenAppSettingsSection("account");
        var section = shell.AppSettingsPage!.FindControl<AccountSettingsSection>("SectionAccount")!;
        var link = section.FindControl<Button>("BtnLinkDiscord")!;
        Click(link);
        await Until(() => told != null, "a link-failed message");
        Assert.Equal(Loc.GetF("account_link_failed_generic", "discord"), told);
        Assert.False(AccountSeed.Discord!.IsVerifying);
        await Until(() => link.IsEnabled, "the button comes back");
    });

    /// <summary>A Link left waiting on the browser ends when the section leaves the tree (review P3): the latch and
    /// callback port are freed at once, with no failure message.</summary>
    [Fact]
    public Task LeavingTheSectionEndsAWaitingLink() => WithShell(async (shell, s) =>
    {
        s.UnifiedId = "u_link";
        LoginDialog.ProxyReachable = _ => Task.FromResult(true);
        string? told = null;
        AccountSettingsSection.Tell = (_, _, body) => { told = body; return Task.CompletedTask; };
        shell.OpenAppSettingsSection("account");
        var section = shell.AppSettingsPage!.FindControl<AccountSettingsSection>("SectionAccount")!;
        await Until(() => section.IsAttachedToVisualTree(), "the Account section is on screen");
        Click(section.FindControl<Button>("BtnLinkDiscord")!);
        await Until(() => AccountSeed.Discord!.IsVerifying, "the Link flow waits on the browser");
        ((Panel)section.Parent!).Children.Remove(section);
        Assert.False(section.IsAttachedToVisualTree(), "detached after");
        await Until(() => !AccountSeed.Discord!.IsVerifying, "leaving the section ended the flow");
        Assert.Null(told);
    });

    /// <summary>WPF UpdateOfflineModeUI greys the Home Login button too (MainWindow.UiUpdates.cs, "Unified login button").</summary>
    [Fact]
    public Task OfflineModeGreysTheHomeLoginButton() => WithShell((shell, s) =>
    {
        var login = shell.Named<SettingsTabView>("SettingsTab")!.FindControl<Button>("BtnUnifiedLogin")!;
        s.OfflineMode = true;
        shell.SyncOfflineModeState();
        Assert.False(login.IsEnabled);
        s.OfflineMode = false;
        shell.SyncOfflineModeState();
        Assert.True(login.IsEnabled);
        return Task.CompletedTask;
    });

    /// <summary>A sandbox instance on the user's desktop was taken for the real app. Its windows say so.</summary>
    [Fact]
    public Task ASandboxRunIsMarkedOnEveryWindow() => WithShell((shell, _) =>
    {
        Assert.True(shell.Named<Border>("SandboxBadge")!.IsVisible);
        try
        {
            SandboxMarker.Install();
            var w = new Window { Title = "Conditioning Control Panel" };
            Assert.Equal("[SANDBOX] Conditioning Control Panel", w.Title);
            w.Title = "[SANDBOX] again";
            Assert.Equal("[SANDBOX] again", w.Title); // never doubled
        }
        finally { SandboxMarker.Enabled = false; }
        return Task.CompletedTask;
    });

    /// <summary>The signed-in title-bar account chip opens Settings at Account (WPF MainWindow.UiUpdates.cs:508; signed
    /// out it opens the login dialog instead). On the first
    /// open of a launch the page had never been measured, the retry ran before the layout pass, and the user landed
    /// on General with no login in sight.</summary>
    [Fact]
    public Task TheAccountChipLandsOnAccountOnTheFirstOpen() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var oldLogin = CoreAccount.IsLoggedInProvider;
        CoreAccount.IsLoggedInProvider = () => true;
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
            CoreAccount.IsLoggedInProvider = oldLogin;
            shell.Close();
            Dispatcher.UIThread.RunJobs();
        }
    });
}
