using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class TakeoverWallpaperWarningTests
{
    [Theory]
    [InlineData(".JPG", true)]
    [InlineData(".jpeg", true)]
    [InlineData(".PNG", true)]
    [InlineData(".bmp", true)]
    [InlineData(".gif", false)]
    [InlineData(".webp", false)]
    [InlineData(".txt", false)]
    public void WallpaperProbeUsesWpfFormatsRecursively(string extension, bool supported)
    {
        var root = Path.Combine(Path.GetTempPath(), "ccp-wallpaper-probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.False(MainShellWindow.WallpaperFolderHasImage(root));
            Directory.CreateDirectory(Path.Combine(root, "nested"));
            Assert.False(MainShellWindow.WallpaperFolderHasImage(root));
            File.WriteAllText(Path.Combine(root, "nested", "image" + extension), "probe");
            Assert.Equal(supported, MainShellWindow.WallpaperFolderHasImage(root));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    // WPF MainWindow.StartStop.cs:211-243,286-311 and MainWindow.Autonomy.cs:420-435.
    [Theory]
    [InlineData("local", "", false, true)]
    [InlineData("LOCAL", "", false, true)]
    [InlineData("online", "", false, false)]
    [InlineData("mixed", "", false, false)]
    [InlineData("local", ".gif", false, true)]
    [InlineData("local", ".webp", false, true)]
    [InlineData("local", ".JPG", false, false)]
    [InlineData("local", ".jpeg", false, false)]
    [InlineData("local", ".PNG", false, false)]
    [InlineData("local", ".bmp", false, false)]
    [InlineData("local", ".png", true, false)]
    [InlineData("local", "", true, true)]
    public Task KeepWarnsOnlyForAnEmptyLocalWallpaperPool(string source, string extension,
        bool missingCustom, bool warn) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var root = Path.Combine(Path.GetTempPath(), "ccp-wallpaper-" + Guid.NewGuid().ToString("N"));
        var custom = Path.Combine(root, "custom");
        var fallback = Path.Combine(root, "wallpapers");
        Directory.CreateDirectory(Path.Combine(missingCustom ? fallback : custom, "nested"));
        if (extension.Length > 0)
            File.WriteAllText(Path.Combine(missingCustom ? fallback : custom, "nested", "image" + extension), "probe");
        // An existing custom folder wins even when the default pool contains an image.
        if (!missingCustom)
        {
            Directory.CreateDirectory(fallback);
            File.WriteAllText(Path.Combine(fallback, "default.png"), "probe");
        }
        var provider = CorePaths.EffectiveAssetsProvider;
        var opener = ExternalOpener.Shell;
        var hostField = typeof(ConditioningControlPanel.Avalonia.Helpers.NotificationService)
            .GetField("_host", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var oldHost = hostField.GetValue(AvApp.Notifications);
        var host = new StackPanel();
        MainShellWindow? shell = null;
        try
        {
            shell = new MainShellWindow();
            CorePaths.EffectiveAssetsProvider = () => root;
            var s = CoreSettings.Current;
            s.MediaSource = source;
            s.WallpaperSourceFolder = custom;
            s.WallpaperEnabled = false;
            shell.Show();
            shell.ShowTab("bambitakeover");
            Dispatcher.UIThread.RunJobs();
            AvApp.Notifications.AttachHost(host);
            host.Children.Clear();
            var tab = shell.GetLogicalDescendants().OfType<BambiTakeoverTabView>().Single();
            // Repainting the tab must not earn a notice.
            tab.IsVisible = false;
            tab.IsVisible = true;
            Assert.Empty(host.Children);
            var keep = tab.FindControl<CheckBox>("ChkWallpaperKeep")!;
            keep.IsChecked = true;
            Assert.True(s.WallpaperEnabled);
            Assert.False(tab.FindControl<Control>("PanelWallpaperDuration")!.IsVisible);
            if (warn)
            {
                var toast = Assert.IsType<Border>(Assert.Single(host.Children));
                Assert.Equal(TimeSpan.FromSeconds(18), Assert.IsType<DispatcherTimer>(toast.Tag).Interval);
                Assert.Equal(Color.Parse("#FFB347"), Assert.IsType<SolidColorBrush>(toast.BorderBrush).Color);
                Assert.Contains(toast.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text ==
                    string.Format(Loc.Get("msg_no_wallpaper_images"), Loc.Get("nav_door_library"), Loc.Get("tab_assets")));
                var buttons = toast.GetLogicalDescendants().OfType<Button>().ToArray();
                string? opened = null;
                ExternalOpener.Shell = target => { opened = target; return true; };
                buttons.Single(b => b.Content is TextBlock t && t.Text == Loc.Get("btn_get_packs"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(DiscordLinks.PackCatalogue, opened);
                buttons.Single(b => b.Content is TextBlock t && t.Text == Loc.Get("btn_open_media_manager"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(shell.GetLogicalDescendants().OfType<Control>().First(c => c.Name == "AssetsTab").IsVisible);
            }
            else Assert.Empty(host.Children);
            foreach (var toast in host.Children.OfType<Border>()) (toast.Tag as DispatcherTimer)?.Stop();
            host.Children.Clear();
            keep.IsChecked = false;
            Assert.Empty(host.Children);
            Assert.True(tab.FindControl<Control>("PanelWallpaperDuration")!.IsVisible);
        }
        finally
        {
            foreach (var toast in host.Children.OfType<Border>()) (toast.Tag as DispatcherTimer)?.Stop();
            shell?.Close();
            hostField.SetValue(AvApp.Notifications, oldHost);
            CorePaths.EffectiveAssetsProvider = provider;
            ExternalOpener.Shell = opener;
            Directory.Delete(root, true);
        }
        return Task.CompletedTask;
    });
}
