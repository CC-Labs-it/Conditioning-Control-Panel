using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class StickyNotificationTests
{
    [Fact]
    public Task StickyQueueDeduplicatesNeverExpiresAndManualDismissalPersists() => Run(() =>
    {
        var key = "sticky-" + Guid.NewGuid();
        var service = new NotificationService { ScheduleRemoval = (remove, _) => remove() };
        var host = new StackPanel();
        using var cleanup = new KeyCleanup(key);
        service.ShowSticky(key, "accepted", NotificationType.Success);
        service.ShowSticky(key, "duplicate");
        service.AttachHost(host);
        var toast = Assert.IsType<Border>(Assert.Single(host.Children));
        Assert.Null(toast.Tag); // No DispatcherTimer: this survives the previous 15-second expiry.
        service.ShowSticky(key, "duplicate");
        Assert.Single(host.Children);
        toast.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ToastDismiss")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Empty(host.Children);
        Assert.Contains(key, CoreSettings.Current.DismissedNotificationKeys);
        service.ShowSticky(key, "should stay dismissed");
        Assert.Empty(host.Children);
        var nextSession = new NotificationService { ScheduleRemoval = (remove, _) => remove() };
        nextSession.ShowSticky(key, "still dismissed");
        nextSession.AttachHost(host);
        Assert.Empty(host.Children);
    });

    [Fact]
    public Task ProgrammaticAndActionDismissalsAllowTheConditionToRecur() => Run(() =>
    {
        var key = "sticky-" + Guid.NewGuid();
        using var cleanup = new KeyCleanup(key);
        var service = new NotificationService { ScheduleRemoval = (remove, _) => remove() };
        var host = new StackPanel();
        service.ShowSticky(key, "queued");
        service.Dismiss(key);
        service.AttachHost(host);
        Assert.Empty(host.Children);
        service.ShowSticky(key, "visible");
        service.Dismiss(key);
        Assert.Empty(host.Children);
        var invoked = 0;
        service.ShowSticky(key, "action", actionLabel: "View", action: () => invoked++);
        host.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ToastAction")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, invoked);
        Assert.Empty(host.Children);
        Assert.DoesNotContain(key, CoreSettings.Current.DismissedNotificationKeys);
        service.ShowSticky(key, "recurring");
        Assert.Single(host.Children);
        service.Dismiss(key);
    });

    private static Task Run(Action body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        body();
        return Task.CompletedTask;
    });

    private sealed class KeyCleanup(string key) : IDisposable
    {
        public void Dispose() => CoreSettings.Current.DismissedNotificationKeys.Remove(key);
    }
}
