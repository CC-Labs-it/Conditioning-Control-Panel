using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Helpers;
using NotificationType = ConditioningControlPanel.Avalonia.Helpers.NotificationType;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Stacking, expiry, action invoke and dismiss of the ported WPF corner toasts.</summary>
public sealed class NotificationServiceTests
{
    [Fact]
    public async Task ToastsQueueStackExpireAndInvokeTheirAction()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();

            var service = new NotificationService();
            var host = new StackPanel();
            var window = new Window { Width = 600, Height = 400, Content = host };
            window.Show();

            // Fired before the host exists: replayed on attach, in order (WPF AttachHost).
            service.Show("first", NotificationType.Info, TimeSpan.FromMilliseconds(150));
            Assert.Empty(host.Children);
            service.AttachHost(host);
            var invoked = 0;
            service.Show("second", NotificationType.Warning, TimeSpan.FromSeconds(30), "See tiers", () => invoked++);
            service.Show("third", NotificationType.Error, TimeSpan.FromSeconds(30));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { "first", "second", "third" }, host.Children.Select(Message));
            Assert.Equal(Color.Parse("#FFB347"), ((SolidColorBrush)((Border)host.Children[1]).BorderBrush!).Color);

            // Expiry: 150 ms display + 220 ms fade-out removes the first toast only.
            await Task.Delay(600);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { "second", "third" }, host.Children.Select(Message));

            // Action button runs the callback once and the toast leaves; × dismisses the other.
            Find(host.Children[0], "ToastAction").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Find(host.Children[1], "ToastDismiss").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, invoked);
            await Task.Delay(400);
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(host.Children);
            window.Close();
        });
    }

    [Fact]
    public Task StickyToastsQueueInOrderDeduplicateAndHaveNoExpiry() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var key = "sticky-queue-" + Guid.NewGuid();
        var service = new NotificationService();
        var host = new StackPanel();
        service.Show("ordinary", duration: TimeSpan.FromSeconds(30));
        service.ShowSticky(key, "accepted");
        service.ShowSticky(key, "duplicate");
        service.ShowSticky(" ", "invalid");
        service.AttachHost(host);
        service.ShowSticky(key, "duplicate after attach");
        Assert.Equal(new[] { "ordinary", "accepted" }, host.Children.Select(Message));
        Assert.True(Assert.IsType<DispatcherTimer>(host.Children[0].Tag).IsEnabled);
        // Ordinary notifications own their expiry timer; sticky ones never start one.
        Assert.Null(host.Children[1].Tag);
        Find(host.Children[0], "ToastDismiss").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Find(host.Children[1], "ToastDismiss").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        CoreSettings.Current.DismissedNotificationKeys.Remove(key);
        return Task.CompletedTask;
    });

    [Fact]
    public Task StickyDismissalPersistsAndSuppressesTheNextServiceInstance() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var oldProvider = CoreSettings.ServiceProvider;
        var key = "sticky-dismiss-" + Guid.NewGuid();
        var settings = new SettingsService(); // CCP_USERDATA_DIR sandbox, never the real profile
        CoreSettings.ServiceProvider = () => settings;
        try
        {
            var host = new StackPanel();
            var service = new NotificationService();
            service.AttachHost(host);
            service.ShowSticky(key, "accepted");
            Find(Assert.Single(host.Children), "ToastDismiss").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains(key, settings.Current.DismissedNotificationKeys);
            Assert.True(settings.HasPendingSave);
            settings.SaveImmediate();
            var reloaded = new SettingsService();
            Assert.Contains(key, reloaded.Current.DismissedNotificationKeys);
            CoreSettings.ServiceProvider = () => reloaded;
            var next = new NotificationService();
            var nextHost = new StackPanel();
            next.ShowSticky(key, "before attach");
            next.AttachHost(nextHost);
            next.ShowSticky(key, "after attach");
            Assert.Empty(nextHost.Children);
            reloaded.Current.DismissedNotificationKeys.Remove(key);
            reloaded.SaveImmediate();
        }
        finally { CoreSettings.ServiceProvider = oldProvider; }
        return Task.CompletedTask;
    });

    [Fact]
    public async Task StickyActionClearsWithoutRememberingDismissalEvenWhenItThrows()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var key = "sticky-action-" + Guid.NewGuid();
            var service = new NotificationService();
            var host = new StackPanel();
            service.AttachHost(host);
            var invoked = 0;
            service.ShowSticky(key, "accepted", actionLabel: "View", action: () =>
            {
                invoked++;
                throw new InvalidOperationException("test callback");
            });
            var toast = Assert.Single(host.Children);
            Find(toast, "ToastAction").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, invoked);
            Assert.False(toast.IsHitTestVisible);
            Assert.DoesNotContain(key, CoreSettings.Current.DismissedNotificationKeys);
            // The old toast is still fading (removal covered above); the key is free again at once.
            service.ShowSticky(key, "condition recurred");
            Assert.Equal("condition recurred", Message(host.Children[^1]));
            Find(host.Children[^1], "ToastDismiss").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            CoreSettings.Current.DismissedNotificationKeys.Remove(key);
            return Task.CompletedTask;
        });
    }

    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static string Message(Control toast) =>
        ((TextBlock)((Grid)((Border)toast).Child!).Children[0]).Text!;

    private static Button Find(Control toast, string name) =>
        ((Grid)((Border)toast).Child!).Children.OfType<StackPanel>().Single().Children.OfType<Button>().Single(c => c.Name == name);
}
