using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class FavoritesDtrhCardTests
{
    [Fact]
    public void PlayCardPinsAndUsesTheAccountAndDailyFreeGates()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();

            var settingsBefore = CoreSettings.ServiceProvider;
            var accountBefore = CoreAccount.IsLoggedInProvider;
            var labBefore = CoreEntitlement.HasLabProvider;
            var freeBefore = CoreEntitlement.IsFreeTodayProvider;
            var gamesBefore = SettingsPaletteIndex.GameAvailableProvider;
            var launchBefore = PlayTabView.LaunchGame;
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            bool signedIn = false, lab = false, free = false;
            CoreAccount.IsLoggedInProvider = () => signedIn;
            CoreEntitlement.HasLabProvider = () => lab;
            CoreEntitlement.IsFreeTodayProvider = id => id == "dtrh" && free;
            SettingsPaletteIndex.GameAvailableProvider = id => id == "dtrh";
            var launched = new List<string>();
            PlayTabView.LaunchGame = launched.Add;
            service.Current.RailFavorites.Clear();
            MainShellWindow? shell = null;
            try
            {
                shell = new MainShellWindow();
                shell.Show();
                shell.ShowTab("play");
                Dispatcher.UIThread.RunJobs();
                var play = shell.Named<PlayTabView>("PlayTab")!;
                var card = Assert.IsType<Border>(Assert.Single(play.FindControl<Grid>("SlotDtrh")!.Children));
                Assert.Same(play.Resources["PlayCardT2"], card.Theme);
                Assert.Equal(new Thickness(3), card.BorderThickness);
                Assert.Equal(2, TierFxBorder.GetTier(card));
                Assert.Equal(3, TierFxBorder.GetRimThickness(card));
                var button = play.FindControl<Button>("BtnPlayDtrh")!;
                var band = play.FindControl<Border>("PlayLockDtrh")!;
                var badge = play.FindControl<TierBadge>("PlayBadgeDtrh")!;
                Assert.True(band.IsVisible);
                Assert.False(band.IsHitTestVisible);
                Assert.False(badge.FreeToday);
                Assert.NotNull(play.FindControl<Border>("PlayDtrhHeroPlate")!.Background);

                var menu = Assert.IsType<ContextMenu>(button.ContextMenu);
                button.RaiseEvent(new ContextRequestedEventArgs());
                Dispatcher.UIThread.RunJobs();
                var item = Assert.IsType<MenuItem>(Assert.Single(menu.Items));
                Assert.Equal(Loc.Get("rail_pin"), item.Header);
                item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                menu.Close();
                Assert.Contains("game.dtrh", service.Current.RailFavorites);
                var dash = shell.Named<SettingsTabView>("SettingsTab")!;
                var favorites = dash.FindControl<StackPanel>("FavoritesList")!;
                Assert.Contains(favorites.Children, c => Equals(c.Tag, "game.dtrh"));
                button.RaiseEvent(new ContextRequestedEventArgs());
                Dispatcher.UIThread.RunJobs();
                item = Assert.IsType<MenuItem>(Assert.Single(menu.Items));
                Assert.Equal(Loc.Get("rail_unpin"), item.Header);
                item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                menu.Close();
                Assert.DoesNotContain("game.dtrh", service.Current.RailFavorites);

                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.Empty(launched);
                Assert.Single(shell.OwnedWindows.OfType<LoginDialog>()).Close();
                signedIn = true;
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(new[] { "dtrh" }, launched);

                free = true;
                play.RefreshPlayCards();
                Assert.False(band.IsVisible);
                Assert.True(badge.FreeToday);
                lab = true;
                play.RefreshPlayCards();
                Assert.False(badge.FreeToday);
                free = false;
                play.RefreshPlayCards();
                Assert.False(band.IsVisible);
            }
            finally
            {
                if (shell != null) { foreach (var w in shell.OwnedWindows.ToArray()) w.Close(); shell.Close(); }
                PlayTabView.LaunchGame = launchBefore;
                SettingsPaletteIndex.GameAvailableProvider = gamesBefore;
                CoreEntitlement.HasLabProvider = labBefore;
                CoreEntitlement.IsFreeTodayProvider = freeBefore;
                CoreAccount.IsLoggedInProvider = accountBefore;
                CoreSettings.ServiceProvider = settingsBefore;
            }
        });
    }
}
