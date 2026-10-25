using System;
using System.Linq;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class AccountOnboardingTests
{
    [Fact]
    public void SignedOutHeaderChipOpensLoginAndSignedInChipOpensAccount() => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureApp();
        var old = CoreAccount.IsLoggedInProvider;
        MainShellWindow? shell = null;
        try
        {
            CoreAccount.IsLoggedInProvider = () => false;
            shell = new MainShellWindow();
            shell.Show();
            shell.FindControl<Button>("BtnAccountChip")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var login = Assert.Single(shell.OwnedWindows.OfType<LoginDialog>());
            login.Close(false);
            Dispatcher.UIThread.RunJobs();
            CoreAccount.IsLoggedInProvider = () => true;
            shell.FindControl<Button>("BtnAccountChip")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(shell.OwnedWindows.OfType<LoginDialog>());
            Assert.True(shell.AppSettingsPage!.IsVisible);
            CoreAccount.IsLoggedInProvider = () => false;
            var section = shell.AppSettingsPage.FindControl<ConditioningControlPanel.Avalonia.Views.Controls.AppSettings.AccountSettingsSection>("SectionAccount")!;
            section.RefreshTierBadge();
            var card = section.FindControl<Border>("AccountTierCard")!;
            Assert.NotNull(card.Cursor);
            var point = card.TranslatePoint(new Point(10, 10), shell)!.Value;
            shell.MouseDown(point, MouseButton.Left);
            shell.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Single(shell.OwnedWindows.OfType<LoginDialog>()).Close(false);
        }
        finally { shell?.Close(); Dispatcher.UIThread.RunJobs(); CoreAccount.IsLoggedInProvider = old; }
    });

    [Fact]
    public void SandboxWhitelistedTierCardStaysInert() => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureApp();
        var old = CoreAccount.IsLoggedInProvider;
        var whitelisted = typeof(AccountSeed).GetProperty(nameof(AccountSeed.SandboxWhitelisted),
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!;
        var oldWhitelisted = AccountSeed.SandboxWhitelisted;
        MainShellWindow? shell = null;
        try
        {
            CoreAccount.IsLoggedInProvider = () => false;
            whitelisted.SetValue(null, true); // Debug-only in the app; tests run Release, so set it directly
            shell = new MainShellWindow();
            shell.Show();
            shell.OpenAppSettingsSection("account");
            Dispatcher.UIThread.RunJobs();
            var section = shell.AppSettingsPage!.FindControl<ConditioningControlPanel.Avalonia.Views.Controls.AppSettings.AccountSettingsSection>("SectionAccount")!;
            section.RefreshTierBadge();
            var card = section.FindControl<Border>("AccountTierCard")!;
            Assert.Null(card.Cursor);
            var point = card.TranslatePoint(new Point(10, 10), shell)!.Value;
            shell.MouseDown(point, MouseButton.Left);
            shell.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(shell.OwnedWindows.OfType<LoginDialog>());
        }
        finally
        {
            shell?.Close(); Dispatcher.UIThread.RunJobs();
            whitelisted.SetValue(null, oldWhitelisted);
            CoreAccount.IsLoggedInProvider = old;
        }
    });

    [Fact]
    public void DeviceCodeTitleUsesTheSelectedLanguage() => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureApp();
        var loc = LocalizationManager.Instance;
        var old = loc.CurrentLanguage;
        try
        {
            loc.SetLanguage("de");
            var dialog = new LoginDialog();
            dialog.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Loc.Get("login_device_title"), dialog.FindControl<Button>("BtnLoginDeviceCode")!
                .GetVisualDescendants().OfType<TextBlock>().Single().Text);
            dialog.Close();
        }
        finally { loc.SetLanguage(old); }
    });

    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont()
                .With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }
}
