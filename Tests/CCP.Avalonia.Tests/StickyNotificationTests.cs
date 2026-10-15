using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class StickyNotificationTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static Button Button(Control toast, string name) =>
        toast.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == name);

    [Fact]
    public void PinkRushUsesPassiveCornerPlacementBeforeShowing()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            Setup();
            var popup = new PinkRushPopup(DateTime.Now.AddMinutes(1));
            try
            {
                var screen = popup.Screens.Primary!;
                var expected = AchievementPopup.CornerRect(screen.WorkingArea, screen.Scaling, popup.Width, popup.Height, 20);
                Assert.Equal(expected.Position, popup.Position);
                Assert.Equal(WindowStartupLocation.Manual, popup.WindowStartupLocation);
                Assert.False(popup.ShowActivated);
                Assert.False(popup.ShowInTaskbar);
            }
            finally { popup.Close(); }
        });
    }

    [Fact]
    public void StickyQueueDeduplicatesAndDismissalSurvivesSettingsReload()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            Setup();
            var oldProvider = CoreSettings.ServiceProvider;
            var settings = new SettingsService();
            CoreSettings.ServiceProvider = () => settings;
            var service = new NotificationService { RemoveLater = (a, _) => a() };
            var host = new StackPanel();
            var window = new Window { Content = host };
            try
            {
                var key = "sticky_" + Guid.NewGuid().ToString("N");
                service.ShowSticky("", "invalid");
                service.ShowSticky("cancelled", "cancelled");
                service.Dismiss("cancelled");
                service.ShowSticky(key, "accepted");
                service.ShowSticky(key, "duplicate");
                service.AttachHost(host);
                window.Show();
                Assert.Single(host.Children);
                Assert.Null(((Border)host.Children.Single()).Tag); // no expiry timer: stays until answered

                // A programmatic resolution does not permanently suppress a recurring condition.
                service.Dismiss(key);
                Assert.Empty(host.Children);
                Assert.DoesNotContain(key, settings.Current.DismissedNotificationKeys);
                service.ShowSticky(key, "returned");
                var dismissed = host.Children.Single();
                Button(dismissed, "ToastDismiss").RaiseEvent(new RoutedEventArgs(global::Avalonia.Controls.Button.ClickEvent));
                CoreSettings.SaveImmediate();
                Assert.Empty(host.Children);
                var reloaded = new SettingsService();
                Assert.Contains(key, reloaded.Current.DismissedNotificationKeys);
                CoreSettings.ServiceProvider = () => reloaded;
                var nextSession = new NotificationService();
                nextSession.ShowSticky(key, "must stay dismissed");
                nextSession.AttachHost(host);
                Assert.Empty(host.Children);
            }
            finally
            {
                window.Close();
                settings.SaveImmediate();
                CoreSettings.ServiceProvider = oldProvider;
            }
        });
    }

    [Fact]
    public void StickyActionClearsOnlyThisSessionAndSecondaryActionReplays()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            Setup();
            var service = new NotificationService { RemoveLater = (a, _) => a() };
            var host = new StackPanel();
            var window = new Window { Content = host };
            var key = "action_" + Guid.NewGuid().ToString("N");
            var primary = 0;
            var secondary = 0;
            try
            {
                service.Show("choose", duration: TimeSpan.FromSeconds(30), actionLabel: "primary_1", action: () => primary++,
                    secondaryActionLabel: "secondary_2", secondaryAction: () => { secondary++; throw new InvalidOperationException("action failed"); });
                service.AttachHost(host);
                window.Show();
                var toast = host.Children.Single();
                var grid = (Grid)((Border)toast).Child!;
                Assert.Equal(2, grid.RowDefinitions.Count);
                Assert.Equal("secondary_2", ((TextBlock)Button(toast, "ToastSecondaryAction").Content!).Text);
                Button(toast, "ToastSecondaryAction").RaiseEvent(new RoutedEventArgs(global::Avalonia.Controls.Button.ClickEvent));
                Assert.Empty(host.Children); // a failing action still dismisses the toast
                Assert.Equal(0, primary);
                Assert.Equal(1, secondary);

                service.ShowSticky(key, "resolve", actionLabel: "resolve", action: () => primary++);
                Button(host.Children.Single(), "ToastAction").RaiseEvent(new RoutedEventArgs(global::Avalonia.Controls.Button.ClickEvent));
                Assert.Empty(host.Children);
                Assert.Equal(1, primary);
                Assert.DoesNotContain(key, CoreSettings.Current.DismissedNotificationKeys);
                service.ShowSticky(key, "recurs");
                Assert.Single(host.Children);
                service.Dismiss(key);
                Assert.Empty(host.Children);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void FadingToastLeavesItsOriginalHostAfterAnotherHostAttaches()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            Setup();
            Action? removal = null;
            var service = new NotificationService { RemoveLater = (a, _) => removal = a };
            var oldHost = new StackPanel();
            var newHost = new StackPanel();
            var window = new Window { Content = oldHost };
            try
            {
                window.Show();
                service.AttachHost(oldHost);
                service.Show("old", duration: TimeSpan.FromSeconds(30));
                Button(oldHost.Children.Single(), "ToastDismiss").RaiseEvent(new RoutedEventArgs(global::Avalonia.Controls.Button.ClickEvent));
                service.AttachHost(newHost);
                service.Show("new", duration: TimeSpan.FromSeconds(30));
                removal!(); // the fade ends after the new host attached
                Assert.Empty(oldHost.Children);
                Assert.Single(newHost.Children);
            }
            finally
            {
                foreach (var toast in oldHost.Children.Concat(newHost.Children).ToArray())
                    Button(toast, "ToastDismiss").RaiseEvent(new RoutedEventArgs(global::Avalonia.Controls.Button.ClickEvent));
                window.Close();
            }
        });
    }
}
