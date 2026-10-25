using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ProfilePrivacyCompletionTests
{
    private static Task Run(Func<MainShellWindow, DiscordTabView, Task> test) =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var oldSettings = CoreSettings.ServiceProvider;
            var oldGet = CoreSecrets.RetrieveProvider;
            var oldSet = CoreSecrets.StoreProvider;
            var settings = new SettingsService();
            CoreSettings.ServiceProvider = () => settings;
            var secrets = new Dictionary<string, string?>
            {
                ["discord_auth"] = JsonConvert.SerializeObject(new DiscordTokenData { AccessToken = "test", ExpiresAt = DateTime.UtcNow.AddHours(1) }),
                ["discord_cache"] = JsonConvert.SerializeObject(new DiscordCachedState { UserId = "123456", CacheExpiresAt = DateTime.UtcNow.AddHours(1) }),
            };
            CoreSecrets.RetrieveProvider = key => secrets.GetValueOrDefault(key);
            CoreSecrets.StoreProvider = (key, value) => secrets[key] = value;
            StartupLadder.ResetForTests();
            MainShellWindow? shell = null;
            try
            {
                Assert.True(AccountSeed.Seed(p => new ProviderSubscription(p, () => settings.Current)));
                shell = new MainShellWindow();
                var tab = shell.FindControl<DiscordTabView>("DiscordTab")!;
                await test(shell, tab);
            }
            finally
            {
                shell?.Close();
                StartupLadder.ResetForTests();
                CoreSettings.ServiceProvider = oldSettings;
                CoreSecrets.RetrieveProvider = oldGet;
                CoreSecrets.StoreProvider = oldSet;
                AccountSeed.Seed();
            }
        });

    [Fact]
    public Task LinkNoticeUsesTheLongLivedPanelsAccountFlowAndTracksLinkedState() => Run((shell, tab) =>
    {
        CoreSettings.Current.OfflineMode = false;
        CoreSettings.Current.HasLinkedDiscord = false;
        tab.RefreshPrivacyActions();
        Assert.True(tab.FindControl<Border>("ProfileLinkNotice")!.IsVisible);
        var calls = 0;
        tab.PrivacyPanel.DiscordAction = host => { Assert.Same(shell, host); calls++; return Task.CompletedTask; };
        shell.Show();
        tab.FindControl<Button>("BtnProfileLinkNotice")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, calls);
        CoreSettings.Current.HasLinkedDiscord = true;
        shell.UpdateQuickLoginUI();
        Assert.False(tab.FindControl<Border>("ProfileLinkNotice")!.IsVisible);
        return Task.CompletedTask;
    });

    [Fact]
    public Task OwnDmConsentRepaintsImmediatelyAndClickUsesStoredId() => Run((shell, tab) =>
    {
        CoreSettings.Current.AllowDiscordDm = true;
        tab.PrivacyPanel.RefreshPrivacy();
        tab.DisplayOwnProfile();
        var button = tab.FindControl<Button>("BtnProfileDiscord")!;
        Assert.True(button.IsVisible);
        string? opened = null;
        tab.OpenDiscordProfile = url => { opened = url; return Task.FromResult(true); };
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("https://discord.com/users/123456", opened);
        tab.PrivacyPanel.FindControl<CheckBox>("ChkDiscordTabAllowDm")!.IsChecked = false;
        Assert.False(button.IsVisible);
        tab.PrivacyPanel.FindControl<CheckBox>("ChkDiscordTabAllowDm")!.IsChecked = true;
        Assert.True(button.IsVisible);
        tab.DisplayProfileEntry(new LeaderboardRow { DisplayName = "other" });
        Assert.False(button.IsVisible);
        tab.PrivacyPanel.FindControl<CheckBox>("ChkDiscordTabAllowDm")!.IsChecked = false;
        Assert.False(button.IsVisible); // Your consent must not put your DM action on another card.
        return Task.CompletedTask;
    });

    [Fact]
    public Task HiddenShellFilesOfferAndInboxReasksWithoutImplicitConsent() => Run(async (shell, tab) =>
    {
        CoreSettings.Current.DiscordShareAchievements = false;
        var calls = 0;
        await shell.OfferAchievementSharingAsync(() => { calls++; return Task.FromResult(true); });
        Assert.Equal(0, calls);
        Assert.False(CoreSettings.Current.DiscordShareAchievements);
        var row = Assert.Single(StartupLadder.Inbox.Items);
        row.Open();
        Assert.Equal(1, calls);
        Assert.True(CoreSettings.Current.DiscordShareAchievements);
        Assert.True(tab.PrivacyPanel.FindControl<CheckBox>("ChkDiscordTabShareAchievements")!.IsChecked);
        await shell.OfferAchievementSharingAsync(() => throw new Exception("Already shared"), true);
        CoreSettings.Current.DiscordShareAchievements = false;
        await shell.OfferAchievementSharingAsync(() => Task.FromResult(false), true);
        Assert.False(CoreSettings.Current.DiscordShareAchievements);
    });

    [Fact]
    public Task PanicDismissesOfferWithoutGrantingConsent() => Run(async (shell, tab) =>
    {
        CoreSettings.Current.DiscordShareAchievements = false;
        shell.Show();
        var ask = shell.OfferAchievementSharingAfterDiscordLinkAsync(userAsked: true);
        Assert.False(ask.IsCompleted);
        MainShellWindow.CloseAchievementSharingForPanic();
        await ask;
        Assert.False(CoreSettings.Current.DiscordShareAchievements);
        Assert.Contains(PanicSurfaces.All, surface => surface.Id == "discord-share-prompt");
    });
}
