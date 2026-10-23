using System;
using System.Linq;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;
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
    public Task RequiredNameKeepsRefusalInlineThenAcceptsOurOwnName() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        EnsureApp();
        var s = CoreSettings.Current;
        var oldId = s.UnifiedId;
        var oldName = s.UserDisplayName;
        var oldV2 = AccountSeed.NewV2;
        var oldWorld = FriendsLanding.World;
        MainShellWindow? shell = null;
        try
        {
            s.UnifiedId = "name-test";
            s.UserDisplayName = "";
            var wire = new NameServer();
            AccountSeed.NewV2 = () => new V2AuthService(() => s, wire);
            FriendsLanding.World = () => new LandingWorld(false, false, false, true, false, false);
            shell = new MainShellWindow();
            shell.Show();
            shell.ProfilePage!.DisplayOwnProfile();
            Assert.Equal(Loc.Get("account_name_not_set"), shell.ProfilePage.FindControl<TextBlock>("TxtProfileViewerName")!.Text);
            var ask = shell.AskDisplayNameAfterSignInAsync(shell, "", null);
            Dispatcher.UIThread.RunJobs();
            var dialog = Assert.Single(shell.OwnedWindows.OfType<DisplayNameDialog>());
            Assert.False(dialog.FindControl<Button>("BtnCancel")!.IsVisible);
            Assert.True(dialog.FindControl<Button>("BtnClose")!.IsVisible);
            var input = dialog.FindControl<TextBox>("TxtDisplayName")!;
            var confirm = dialog.FindControl<Button>("BtnConfirm")!;
            input.Text = "ab";
            Dispatcher.UIThread.RunJobs();
            Assert.False(confirm.IsEnabled);
            input.Text = "bad!name";
            Dispatcher.UIThread.RunJobs();
            Assert.False(confirm.IsEnabled);
            Assert.Equal(Loc.Get("name_ask_bad_chars"), dialog.FindControl<TextBlock>("TxtStatus")!.Text);
            input.Text = "Taken";
            Dispatcher.UIThread.RunJobs();
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await PumpUntil(() => input.IsEnabled);
            Assert.Equal(new[] { "/v2/user/change-display-name", "/v2/auth/check-name" }, wire.Paths);
            Assert.True(dialog.IsVisible);
            Assert.Equal(Loc.GetF("login_name_already_taken", "Taken"), dialog.FindControl<TextBlock>("TxtStatus")!.Text);
            wire.Accept = true;
            input.Text = "OwnName";
            Dispatcher.UIThread.RunJobs();
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await PumpUntil(() => ask.IsCompleted);
            Assert.Equal("OwnName", s.UserDisplayName);
            Assert.Equal(3, wire.Paths.Count); // availability never runs before the accepted own-name change
            Assert.False(dialog.IsVisible);
            Assert.Equal("OwnName", shell.ProfilePage.FindControl<TextBlock>("TxtProfileViewerName")!.Text);
        }
        finally
        {
            shell?.Close(); Dispatcher.UIThread.RunJobs();
            (s.UnifiedId, s.UserDisplayName) = (oldId, oldName);
            CoreSettings.Save();
            AccountSeed.NewV2 = oldV2;
            FriendsLanding.World = oldWorld;
        }
    });

    [Fact]
    public Task StartupAskWaitsForAQuietProfileLoadAndIsOncePerAccount() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        EnsureApp();
        var s = CoreSettings.Current;
        var oldId = s.UnifiedId;
        var oldName = s.UserDisplayName;
        var oldWorld = FriendsLanding.World;
        MainShellWindow? shell = null;
        try
        {
            s.UnifiedId = "startup-name-test"; s.UserDisplayName = "";
            FriendsLanding.World = () => new LandingWorld(false, false, false, true, false, false, true);
            shell = new MainShellWindow(); shell.Show();
            shell.QueueDisplayNameAskIfNeeded();
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(shell.OwnedWindows.OfType<DisplayNameDialog>());
            FriendsLanding.World = () => new LandingWorld(false, false, false, true, false, false);
            shell.QueueDisplayNameAskIfNeeded();
            await PumpUntil(() => shell.OwnedWindows.OfType<DisplayNameDialog>().Any());
            Assert.Single(shell.OwnedWindows.OfType<DisplayNameDialog>()).Close(false);
            Dispatcher.UIThread.RunJobs();
            shell.QueueDisplayNameAskIfNeeded();
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(shell.OwnedWindows.OfType<DisplayNameDialog>());
        }
        finally
        {
            shell?.Close(); Dispatcher.UIThread.RunJobs();
            (s.UnifiedId, s.UserDisplayName) = (oldId, oldName);
            FriendsLanding.World = oldWorld;
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

    private static async Task PumpUntil(Func<bool> done)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew(); // bounded by time, not turns (P73)
        while (!done() && sw.Elapsed < TimeSpan.FromSeconds(10)) { await Task.Yield(); Dispatcher.UIThread.RunJobs(); }
        Assert.True(done());
    }

    private sealed class NameServer : HttpMessageHandler
    {
        public readonly List<string> Paths = new();
        public bool Accept;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            return Task.FromResult(new HttpResponseMessage(path.EndsWith("check-name") || Accept ? HttpStatusCode.OK : HttpStatusCode.Conflict)
            {
                Content = new StringContent(path.EndsWith("check-name") ? "{\"available\":false}"
                    : Accept ? "{\"new_display_name\":\"OwnName\"}" : "{\"error\":\"Taken\"}")
            });
        }
    }
}
