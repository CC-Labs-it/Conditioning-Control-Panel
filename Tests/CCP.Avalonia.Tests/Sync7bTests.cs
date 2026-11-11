using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;
using IconKind = FluentIcons.Common.Symbol;
namespace CCP.Avalonia.Tests;
public class Sync7bTests
{
    private static void Setup()
    {
        var settings = new ConditioningControlPanel.Services.SettingsService(); CoreSettings.ServiceProvider = () => settings;
        if (Application.Current is null) global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
    private static Task Run(Func<Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var provider = CoreSettings.ServiceProvider;
        try { Setup(); await body(); }
        finally { CoreSettings.ServiceProvider = provider; }
    });
    [Fact]
    public Task SettingsScopeRestoresProviderAfterFailure() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var provider = CoreSettings.ServiceProvider;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(() => { Assert.NotSame(provider, CoreSettings.ServiceProvider); throw new InvalidOperationException(); }));
        Assert.Same(provider, CoreSettings.ServiceProvider);
    });
    [Fact]
    public Task DrawerHandleAndNavigationResetItsPushWidth() => Run(() =>
    {
        var drawer = new PageFavoritesDrawer(); drawer.OnNavigated(ConditioningControlPanel.Services.UI.NavSections.Studio);
        Assert.True(drawer.IsVisible); drawer.Handle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(drawer.IsOpen); Assert.Equal(92, drawer.Body.Width); Assert.Equal(IconKind.ChevronRight, drawer.Chevron.Kind);
        drawer.OnNavigated(ConditioningControlPanel.Services.UI.NavSections.Studio); Assert.False(drawer.IsOpen); Assert.Equal(0, drawer.Body.Width);
        Assert.Equal(IconKind.ChevronLeft, drawer.Chevron.Kind); drawer.OnNavigated(ConditioningControlPanel.Services.UI.NavSections.Home);
        Assert.False(drawer.IsVisible);
        return Task.CompletedTask;
    });
    [Fact]
    public Task DrawerHandleFillsEdgeAndKeepsReferenceTypographyAndInk() => Run(async () =>
    {
        CoreSettings.Current.MotionLevel = ConditioningControlPanel.Models.MotionLevel.Off; Assert.False(AmbientFxCanvas.Env.AllowTransitions);
        var drawer = new PageFavoritesDrawer(); var labels = drawer.GetLogicalDescendants().OfType<TextBlock>().ToArray();
        foreach (var label in labels) label.FontFamily = new FontFamily("DejaVu Sans");
        drawer.Resources["SecondaryBrush"] = Brushes.Red; drawer.Resources["TextMutedBrush"] = Brushes.Blue;
        var host = new Window { Content = drawer, Width = 22, Height = 400 };
        try
        {
            host.Show(); drawer.Measure(new Size(22,400)); drawer.Arrange(new Rect(0,0,22,400));
            Assert.Equal(new Size(18,390), drawer.Handle.Bounds.Size);
            Assert.Contains(drawer.GetLogicalDescendants().OfType<IconGlyph>(), g => g.Kind == IconKind.Star);
            var presenter = Assert.IsType<global::Avalonia.Controls.Presenters.ContentPresenter>(Assert.Single(drawer.Handle.GetVisualChildren()));
            void AssertNoChrome() => Assert.Multiple(
                () => Assert.True(presenter.Background is null || presenter.Background is ISolidColorBrush { Color.A: 0 }),
                () => Assert.Equal(Matrix.Identity, drawer.Handle.RenderTransform?.Value ?? Matrix.Identity));
            AssertNoChrome();
            Assert.Equal(drawer.Handle.Bounds.Size, Assert.IsType<Border>(presenter.Content).Bounds.Size);
            Assert.Equal(8, drawer.FavoritesEmpty.FontSize); Assert.Equal(0.85, drawer.FavoritesEmpty.Opacity); Assert.Same(Brushes.Blue, drawer.FavoritesEmpty.Foreground);
            Assert.Equal(new Thickness(0,0,0,6), drawer.FavoritesEmpty.Margin);
            var caption = labels.Single(t => t.Text == ConditioningControlPanel.Localization.Loc.Get("rail_recent_caption"));
            Assert.Equal(9, caption.FontSize); Assert.Equal(FontWeight.Bold, caption.FontWeight); Assert.Same(Brushes.Red, caption.Foreground);
            Assert.Equal(new Thickness(0,8,0,6), caption.Margin); var hue = Color.FromRgb(100,80,160); drawer.Paint(hue);
            Assert.Equal(ConditioningControlPanel.Avalonia.Views.Tabs.SettingsTabView.Lighten(hue,0.18), Assert.IsType<SolidColorBrush>(drawer.Chevron.Foreground).Color);
            host.MouseMove(new Point(10,20));
            Assert.True(drawer.Handle.IsPointerOver); AssertNoChrome();
            host.MouseDown(new Point(10,20), global::Avalonia.Input.MouseButton.Left); Assert.True(drawer.Handle.IsPressed);
            await Task.Delay(120); AssertNoChrome();
            host.MouseUp(new Point(10,20), global::Avalonia.Input.MouseButton.Left); Assert.False(drawer.Handle.IsPressed); AssertNoChrome();
            Assert.Equal(ConditioningControlPanel.Avalonia.Views.Tabs.SettingsTabView.Lighten(hue,0.55), Assert.IsType<SolidColorBrush>(drawer.Chevron.Foreground).Color);
            host.MouseMove(new Point(110,20));
            Assert.True(drawer.Handle.Focus()); Assert.Null(drawer.Handle.FocusAdorner); Assert.Equal(new global::Avalonia.Input.Cursor(global::Avalonia.Input.StandardCursorType.Hand).ToString(), drawer.Handle.Cursor?.ToString());
            Assert.Equal(ConditioningControlPanel.Avalonia.Views.Tabs.SettingsTabView.Lighten(hue,0.55), Assert.IsType<SolidColorBrush>(drawer.Chevron.Foreground).Color);
        }
        finally { host.Close(); }
    });
    [Fact]
    public Task DrawerSharesPinsPushesPageAndClosesOnNavigation() => Run(() =>
    {
        CoreSettings.Current.RailFavorites.Clear(); var w = new MainShellWindow();
        try
        {
            w.ShowTab("haptics");
            var d = w.PageDrawer!; Assert.True(d.IsVisible);
            w.TogglePinned("tab.haptics");
            Assert.True(d.IsOpen); Assert.Equal("tab.haptics", Assert.Single(d.FavoritesList.Children).Tag);
            Assert.Empty(d.RecentList.Children.Where(c => Equals(c.Tag, "tab.haptics"))); Assert.Equal(92, d.Body.Width);
            Assert.Equal(1, Grid.GetColumn(d)); Assert.Equal(GridLength.Auto, ((Grid)d.Parent!).ColumnDefinitions[1].Width);
            w.ShowTab("haptics");
            Assert.False(d.IsOpen); Assert.Equal(0, d.Body.Width);
            d.Handle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(d.IsOpen);
            w.ShowTab("settings");
            Assert.False(d.IsVisible); Assert.False(d.IsOpen);
        }
        finally { w.Close(); }
        return Task.CompletedTask;
    });
}
