using System;
using System.Linq;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Interactivity;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Views.Controls;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class ProfilePrivacyPanelTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task AchievementOfferAsksWithShellOwnerAndOnlyYesEnablesSharing(bool accept) =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            EnsureApp();
            var old = CoreSettings.Current.DiscordShareAchievements;
            StartupLadder.ResetForTests();
            var shell = new MainShellWindow();
            try
            {
                CoreSettings.Current.DiscordShareAchievements = false;
                shell.Show();
                var task = shell.OfferAchievementSharingAfterDiscordLink(userAsked: true);
                var prompt = Assert.Single(shell.OwnedWindows.OfType<MessageDialog>());
                Assert.Equal(Loc.Get("title_discord_linked"), prompt.Title);
                Assert.Equal(Loc.Get("msg_discord_share_achievements_prompt"), prompt.FindControl<TextBlock>("TxtMessage")!.Text);
                Assert.False(task.IsCompleted);
                prompt.FindControl<Button>(accept ? "BtnOk" : "BtnCancel")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await task;
                Assert.Equal(accept, CoreSettings.Current.DiscordShareAchievements);
                var tab = shell.FindControl<ConditioningControlPanel.Avalonia.Views.Tabs.DiscordTabView>("DiscordTab")!;
                Assert.Equal(accept, tab.PrivacyPanel.FindControl<CheckBox>("ChkDiscordTabShareAchievements")!.IsChecked);
                if (accept)
                {
                    await shell.OfferAchievementSharingAfterDiscordLink();
                    Assert.Empty(shell.OwnedWindows.OfType<MessageDialog>());
                }
            }
            finally { shell.Close(); CoreSettings.Current.DiscordShareAchievements = old; StartupLadder.ResetForTests(); }
        });

    [Fact]
    public Task AchievementOfferParksOnceDuringGameThenInboxAsksAndPanicDeclines() =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            EnsureApp();
            StartupLadder.ResetForTests();
            var old = CoreSettings.Current.DiscordShareAchievements;
            var surfaces = PanicSurfaces.All;
            var shell = new MainShellWindow();
            try
            {
                CoreSettings.Current.DiscordShareAchievements = false;
                shell.Show();
                PanicSurfaces.All = new[] { new PanicSurfaces.Surface("game-test", _ => { }, () => true) };
                await shell.OfferAchievementSharingAfterDiscordLink();
                await shell.OfferAchievementSharingAfterDiscordLink();
                Assert.Empty(shell.OwnedWindows.OfType<MessageDialog>());
                var row = Assert.Single(StartupLadder.Inbox.Items);
                Assert.Equal("discord-share-achievements", row.Key);
                StartupLadder.Inbox.Open(row);
                Assert.Single(shell.OwnedWindows.OfType<MessageDialog>());
                // Restore the production registry: its new entry must close the actual prompt.
                PanicSurfaces.All = surfaces;
                Assert.Single(PanicSurfaces.All.Where(x => x.Id == "achievement-share-prompt")).Stop(shell);
                await Task.Yield();
                Assert.Empty(shell.OwnedWindows.OfType<MessageDialog>());
                Assert.False(CoreSettings.Current.DiscordShareAchievements);
                Assert.Empty(StartupLadder.Inbox.Items);
            }
            finally { shell.Close(); PanicSurfaces.All = surfaces; CoreSettings.Current.DiscordShareAchievements = old; StartupLadder.ResetForTests(); }
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task AchievementOfferWaitsWhenShellIsHiddenOrMinimized(bool minimized) =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            EnsureApp();
            StartupLadder.ResetForTests();
            var old = CoreSettings.Current.DiscordShareAchievements;
            var shell = new MainShellWindow();
            try
            {
                CoreSettings.Current.DiscordShareAchievements = false;
                if (minimized) { shell.Show(); shell.WindowState = WindowState.Minimized; }
                await shell.OfferAchievementSharingAfterDiscordLink();
                Assert.Empty(shell.OwnedWindows.OfType<MessageDialog>());
                Assert.Single(StartupLadder.Inbox.Items);
            }
            finally { shell.Close(); CoreSettings.Current.DiscordShareAchievements = old; StartupLadder.ResetForTests(); }
        });

    [Fact]
    public Task DiscordActionOpensProfileAndFailureCopiesIdForFeedbackInterval() =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            EnsureApp();
            var tab = new ConditioningControlPanel.Avalonia.Views.Tabs.DiscordTabView();
            var button = tab.FindControl<Button>("BtnProfileDiscord")!;
            var label = tab.FindControl<TextBlock>("TxtProfileDiscordId")!;
            button.Tag = "123";
            label.Text = "community-member";
            var copied = new TaskCompletionSource();
            var delay = new TaskCompletionSource();
            tab.OpenDiscord = url => { Assert.Equal("https://discord.com/users/123", url); return Task.FromResult(true); };
            tab.CopyDiscord = _ => throw new Exception("A successful launch must not copy");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("community-member", label.Text);
            tab.OpenDiscord = _ => Task.FromResult(false);
            tab.CopyDiscord = id => { Assert.Equal("123", id); copied.SetResult(); return Task.CompletedTask; };
            tab.CopiedDelay = () => delay.Task;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await copied.Task;
            Assert.Equal(Loc.Get("label_id_copied"), label.Text);
            delay.SetResult();
            await Task.Yield();
            Assert.Equal("community-member", label.Text);
        });

    private static void EnsureApp()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public Task ProfileNoticeTracksLinkStateAndRunsThePrivacyLoginFlow() =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var s = CoreSettings.Current;
            var oldLinked = s.HasLinkedDiscord;
            var oldOffline = s.OfflineMode;
            var shell = new MainShellWindow();
            try
            {
                s.OfflineMode = false;
                s.HasLinkedDiscord = false;
                shell.Show();
                var tab = shell.FindControl<ConditioningControlPanel.Avalonia.Views.Tabs.DiscordTabView>("DiscordTab")!;
                var calls = 0;
                tab.PrivacyPanel.DiscordAction = host => { Assert.Same(shell, host); calls++; return Task.CompletedTask; };
                shell.ShowTab("discord");
                Assert.True(tab.FindControl<Border>("ProfileLinkNotice")!.IsVisible);
                tab.FindControl<Button>("BtnProfileLinkNotice")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(1, calls);
                s.HasLinkedDiscord = true;
                shell.UpdateQuickLoginUI();
                Assert.False(tab.FindControl<Border>("ProfileLinkNotice")!.IsVisible);
                s.OfflineMode = true;
                shell.UpdateQuickLoginUI();
                Assert.False(tab.FindControl<Button>("BtnProfileLinkNotice")!.IsEnabled);
            }
            finally { shell.Close(); s.HasLinkedDiscord = oldLinked; s.OfflineMode = oldOffline; }
            return Task.CompletedTask;
        });

    [Fact]
    public Task OwnCardDmConsentHidesAnyPreviousUsersDiscordAction() =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var s = CoreSettings.Current;
            var oldOffline = s.OfflineMode;
            var oldDm = s.AllowDiscordDm;
            try
            {
                s.OfflineMode = true;
                s.AllowDiscordDm = false;
                var tab = new ConditioningControlPanel.Avalonia.Views.Tabs.DiscordTabView();
                var button = tab.FindControl<Button>("BtnProfileDiscord")!;
                tab.DisplayProfileEntry(new ConditioningControlPanel.Avalonia.Views.Tabs.LeaderboardRow
                    { DisplayName = "community-member", DiscordId = "123" });
                Assert.True(button.IsVisible);
                Assert.Equal("123", button.Tag);
                tab.DisplayOwnProfile();
                Assert.False(button.IsVisible);
                Assert.Null(button.Tag);
            }
            finally { s.OfflineMode = oldOffline; s.AllowDiscordDm = oldDm; }
            return Task.CompletedTask;
        });

    [Fact]
    public Task LocalSharingSwitchesLoadSavedValuesAndPersistIndependentChoices() =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var oldProvider = CoreSettings.ServiceProvider;
            var settings = new SettingsService();
            CoreSettings.ServiceProvider = () => settings;
            try
            {
                var s = settings.Current;
                s.DiscordShareAchievements = true;
                s.DiscordShareLevelUps = false;
                s.GoonRichPresence = true;
                // These independent audiences must not be changed by local sharing controls.
                s.PublicShareRealAvatar = false;
                s.GoonShareAvatar = true;
                var panel = new ProfilePrivacyPanel();
                var achievements = panel.FindControl<CheckBox>("ChkDiscordTabShareAchievements")!;
                var levels = panel.FindControl<CheckBox>("ChkDiscordTabShareLevelUps")!;
                var goon = panel.FindControl<CheckBox>("ChkGoonRichPresence")!;
                Assert.True(achievements.IsChecked);
                Assert.False(levels.IsChecked);
                Assert.True(goon.IsChecked);
                achievements.IsChecked = false;
                levels.IsChecked = true;
                goon.IsChecked = false;
                Assert.False(s.DiscordShareAchievements);
                Assert.True(s.DiscordShareLevelUps);
                Assert.False(s.GoonRichPresence);
                Assert.False(s.PublicShareRealAvatar);
                Assert.True(s.GoonShareAvatar);
                // A new control loads settings rather than the markup defaults.
                var reopened = new ProfilePrivacyPanel();
                Assert.False(reopened.FindControl<CheckBox>("ChkDiscordTabShareAchievements")!.IsChecked);
                Assert.True(reopened.FindControl<CheckBox>("ChkDiscordTabShareLevelUps")!.IsChecked);
                Assert.False(reopened.FindControl<CheckBox>("ChkGoonRichPresence")!.IsChecked);
            }
            finally { CoreSettings.ServiceProvider = oldProvider; }
            return Task.CompletedTask;
        });
    [Fact]
    public Task EachAudienceSwitchPushesItsOwnFieldAndPaintingNeverPushes() =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var oldProvider = CoreSettings.ServiceProvider;
            var oldPush = ProfilePrivacyPanel.PushPrivacy;
            var settings = new SettingsService();
            CoreSettings.ServiceProvider = () => settings;
            var fields = new List<SyncBody.Field>();
            ProfilePrivacyPanel.PushPrivacy = field => { fields.Add(field); return Task.CompletedTask; };
            try
            {
                var panel = new ProfilePrivacyPanel();
                Assert.Empty(fields);
                var names = new[] { "ChkDiscordTabShowOnline", "ChkDiscordTabAllowDm", "ChkDiscordTabSharePfp",
                    "ChkPublicShareRealAvatar", "ChkGoonShareAvatar", "ChkGoonShareDiscordDm" };
                var expected = new[] { SyncBody.Field.ShowOnlineStatus, SyncBody.Field.AllowDiscordDm, SyncBody.Field.ShareProfilePicture,
                    SyncBody.Field.PublicShareAvatar, SyncBody.Field.GoonShareAvatar, SyncBody.Field.GoonShareDm };
                for (int i = 0; i < names.Length; i++)
                {
                    var box = panel.FindControl<CheckBox>(names[i])!;
                    box.IsChecked = box.IsChecked != true;
                    Assert.Equal(expected[i], fields[i]);
                    Assert.Equal(i + 1, fields.Count);
                }
                var s = settings.Current;
                // A real programmatic repaint (attach -> RefreshPrivacy) flips every box and must push nothing.
                s.ShowOnlineStatus = !s.ShowOnlineStatus; s.AllowDiscordDm = !s.AllowDiscordDm;
                s.ShareProfilePicture = !s.ShareProfilePicture; s.PublicShareRealAvatar = !s.PublicShareRealAvatar;
                s.GoonShareAvatar = !s.GoonShareAvatar; s.GoonShareDiscordDm = !s.GoonShareDiscordDm;
                var host = new Window { Content = panel };
                host.Show();
                try
                {
                    var flipped = new[] { s.ShowOnlineStatus, s.AllowDiscordDm, s.ShareProfilePicture,
                        s.PublicShareRealAvatar, s.GoonShareAvatar, s.GoonShareDiscordDm };
                    for (int i = 0; i < names.Length; i++)
                        Assert.Equal(flipped[i], panel.FindControl<CheckBox>(names[i])!.IsChecked);
                    Assert.Equal(6, fields.Count);
                }
                finally { host.Close(); }
                var persisted = new[] { s.ShowOnlineStatus, s.AllowDiscordDm, s.ShareProfilePicture,
                    s.PublicShareRealAvatar, s.GoonShareAvatar, s.GoonShareDiscordDm };
                var reopened = new ProfilePrivacyPanel();
                for (int i = 0; i < names.Length; i++)
                    Assert.Equal(persisted[i], reopened.FindControl<CheckBox>(names[i])!.IsChecked);
                Assert.Equal(6, fields.Count);
            }
            finally { ProfilePrivacyPanel.PushPrivacy = oldPush; CoreSettings.ServiceProvider = oldProvider; }
            return Task.CompletedTask;
        });

    [Fact]
    public Task DiscordButtonStepsAsideForTheShellFlowAndOfflineCannotOpenIt() =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var oldOffline = CoreSettings.Current.OfflineMode;
            var shell = new MainShellWindow();
            var panel = new ProfilePrivacyPanel();
            int calls = 0;
            panel.DiscordAction = host => { Assert.Same(shell, host); calls++; return Task.CompletedTask; };
            var dialog = new ProfilePrivacyDialog(panel);
            try
            {
                CoreSettings.Current.OfflineMode = false;
                shell.Show();
                dialog.Show(shell);
                panel.FindControl<Button>("BtnDiscordTabLogin")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(1, calls);
                Assert.False(dialog.IsVisible);
                var offlineDialog = new ProfilePrivacyDialog(panel);
                CoreSettings.Current.OfflineMode = true;
                offlineDialog.Show(shell);
                Assert.False(panel.FindControl<Button>("BtnDiscordTabLogin")!.IsEnabled);
                await panel.LoginAsync();
                Assert.Equal(1, calls);
                offlineDialog.Close();
            }
            finally { dialog.Close(); shell.Close(); CoreSettings.Current.OfflineMode = oldOffline; }
        });

}
