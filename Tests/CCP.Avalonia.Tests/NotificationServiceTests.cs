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
using ConditioningControlPanel.Avalonia.Helpers;
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
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
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
    public void QueuedToastsReplayInOrderAndNewToastsWaitTheirTurn()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();

            var service = new NotificationService();
            var host = new StackPanel();
            var key = "replay_" + Guid.NewGuid().ToString("N");
            service.Show("first", duration: TimeSpan.FromMinutes(1));
            service.ShowSticky(key, "sticky");
            service.ShowSticky(key, "duplicate");
            service.AttachHost(host);
            Assert.Equal(TimeSpan.FromMilliseconds(900), NotificationService.ReplayGap);
            Assert.Equal(new[] { "first" }, host.Children.Select(Message));
            service.Show("later", duration: TimeSpan.FromMinutes(1));
            Assert.Single(host.Children);
            service.ShowNextPending();
            Assert.Equal(new[] { "first", "sticky" }, host.Children.Select(Message));
            service.ShowNextPending();
            Assert.Equal(new[] { "first", "sticky", "later" }, host.Children.Select(Message));
            service.Show("immediate", duration: TimeSpan.FromMinutes(1));
            Assert.Equal(new[] { "first", "sticky", "later", "immediate" }, host.Children.Select(Message));
            foreach (var toast in host.Children.OfType<Border>())
                (toast.Tag as DispatcherTimer)?.Stop();
        });
    }

    private static string Message(Control toast) =>
        ((TextBlock)((Grid)((Border)toast).Child!).Children[0]).Text!;

    private static Button Find(Control toast, string name) =>
        ((Grid)((Border)toast).Child!).Children.OfType<StackPanel>().Single().Children.OfType<Button>().Single(c => c.Name == name);
}
