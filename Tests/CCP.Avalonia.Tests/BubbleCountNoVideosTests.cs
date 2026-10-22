using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class BubbleCountNoVideosTests
{
    [Fact]
    public Task Empty_library_notice_reaches_the_warning_toast() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var hostField = typeof(NotificationService).GetField("_host", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var previousHost = hostField.GetValue(AvApp.Notifications);
        var previousPacks = ContentPackStore.Current;
        var panel = new StackPanel();
        try
        {
            ContentPackStore.Current = null;
            AvApp.Notifications.AttachHost(panel);
            foreach (var queuedToast in panel.Children.OfType<Border>())
                (queuedToast.Tag as global::Avalonia.Threading.DispatcherTimer)?.Stop();
            panel.Children.Clear(); // Discard unrelated notices queued before this host attached.
            BubbleCountHost.Instance.NoVideos();
            var toast = Assert.IsType<Border>(Assert.Single(panel.Children));
            var text = Assert.IsType<Grid>(toast.Child).Children.OfType<TextBlock>().Single();
            Assert.Equal(Loc.Get("bubblecount_no_videos"), text.Text);
            Assert.Equal(Color.Parse("#FFB347"), Assert.IsType<SolidColorBrush>(toast.BorderBrush).Color);
        }
        finally
        {
            foreach (var toast in panel.Children.OfType<Border>())
                (toast.Tag as global::Avalonia.Threading.DispatcherTimer)?.Stop();
            hostField.SetValue(AvApp.Notifications, previousHost);
            ContentPackStore.Current = previousPacks;
        }
        return Task.CompletedTask;
    });
}
