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
