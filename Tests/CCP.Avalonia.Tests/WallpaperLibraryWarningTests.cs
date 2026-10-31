using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class WallpaperLibraryWarningTests
{
    [Theory]
    [InlineData("local", "", true)]
    [InlineData("local", "nested/one.JPG", false)]
    [InlineData("local", "one.jpeg", false)]
    [InlineData("local", "one.png", false)]
    [InlineData("local", "one.bmp", false)]
    [InlineData("local", "one.webp", true)]
    [InlineData("local", "one.gif", true)]
    [InlineData("online", "", false)]
    [InlineData("mixed", "", false)]
    public void MatchesWpfLibraryProbe(string source, string file, bool empty)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(root);
        try
        {
            CoreSettings.Current.MediaSource = source;
            CoreSettings.Current.WallpaperSourceFolder = root;
            if (file.Length > 0)
            {
                var path = Path.Combine(root, file);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "");
            }
            Assert.Equal(empty, MainShellWindow.WallpaperLibraryEmpty());
            // A missing custom folder falls back to effective assets.
            CorePaths.EffectiveAssetsProvider = () => root;
            CoreSettings.Current.WallpaperSourceFolder = Path.Combine(root, "missing");
            Assert.Equal(source == "local", MainShellWindow.WallpaperLibraryEmpty());
            Directory.CreateDirectory(Path.Combine(root, "wallpapers"));
            File.WriteAllText(Path.Combine(root, "wallpapers", "fallback.PNG"), "");
            Assert.False(MainShellWindow.WallpaperLibraryEmpty());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public Task KeepToggleWarnsOnlyOnArmingAndOpensMediaManager() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(root);
        var shell = new MainShellWindow();
        try
        {
            var s = CoreSettings.Current;
            s.MediaSource = "local";
            s.WallpaperSourceFolder = root;
            s.WallpaperEnabled = false;
            shell.Show();
            shell.ShowTab("bambitakeover");
            Dispatcher.UIThread.RunJobs();
            var host = new StackPanel();
            AvApp.Notifications.AttachHost(host);
            var tab = shell.GetLogicalDescendants().OfType<BambiTakeoverTabView>().Single();
            var keep = tab.FindControl<CheckBox>("ChkWallpaperKeep")!;
            Assert.Empty(host.Children);
            keep.IsChecked = true;
            Assert.True(s.WallpaperEnabled);
            var toast = Assert.IsType<Border>(Assert.Single(host.Children));
            Assert.Equal(TimeSpan.FromSeconds(18), Assert.IsType<DispatcherTimer>(toast.Tag).Interval);
            var controls = toast.GetLogicalDescendants().ToArray();
            Assert.Contains(controls.OfType<TextBlock>(), t => t.Text == string.Format(
                Loc.Get("msg_no_wallpaper_images"), Loc.Get("nav_door_library"), Loc.Get("tab_assets")));
            var buttons = controls.OfType<Button>().ToArray();
            Assert.Contains(buttons, b => b.Name == "ToastSecondaryAction");
            buttons.Single(b => b.Name == "ToastAction").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(shell.GetLogicalDescendants().OfType<AssetsTabView>().Single().IsVisible);
            keep.IsChecked = false;
            Assert.Single(host.Children);
        }
        finally { shell.Close(); Directory.Delete(root, true); }
        return Task.CompletedTask;
    });
}
