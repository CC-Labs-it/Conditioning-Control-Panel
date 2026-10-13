using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Controls.NavRail;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The every-page favourites drawer (owner, 2026-10-13): a 22 px handle on the right edge of
/// every page but Home, a body that pushes the page, put away on every navigation. Realizes the
/// real control beside a stand-in page in the same two-column shape MainWindow's PageAreaGrid
/// uses, and reads MainWindow's source for the wiring a render cannot reach.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class PageFavoritesDrawerTests
{
    private const double PageWidth = 1585 - 96;
    private const double PageHeight = 800;

    private static (Grid host, Border page, PageFavoritesDrawer drawer) Realize()
    {
        var host = new Grid();
        host.ColumnDefinitions.Add(new ColumnDefinition());
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var page = new Border();
        var drawer = new PageFavoritesDrawer();
        Grid.SetColumn(drawer, 1);
        host.Children.Add(page);
        host.Children.Add(drawer);
        Layout(host);
        return (host, page, drawer);
    }

    private static void Layout(Grid host)
    {
        host.Measure(new Size(PageWidth, PageHeight));
        host.Arrange(new Rect(0, 0, PageWidth, PageHeight));
        host.UpdateLayout();
    }

    private static Rect Bounds(FrameworkElement e, Visual root) =>
        e.TransformToAncestor(root).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight));

    [Theory]
    [InlineData(NavSections.Home, false)]
    [InlineData(NavSections.Settings, true)]
    [InlineData("play", true)]
    [InlineData(null, false)]
    public void It_shows_on_every_section_but_Home(string? section, bool shown)
        => Assert.Equal(shown, PageFavoritesDrawer.ShowsOn(section));

    [Fact]
    public void Closed_it_is_a_handle_on_the_right_edge_and_open_it_pushes_the_page() => WpfRenderHarness.OnStaThread(() =>
    {
        var (host, page, drawer) = Realize();
        Assert.False(drawer.IsOpen);
        Assert.Equal(0, drawer.Body.ActualWidth);
        var handle = Bounds(drawer.Handle, host);
        Assert.InRange(handle.Right, PageWidth - 3, PageWidth);
        Assert.InRange(handle.Width, 16, 22);
        Assert.True(handle.Height > 400, $"the handle is only {handle.Height} tall");
        Assert.Equal(PageWidth - PageFavoritesDrawer.HandleColumn, page.ActualWidth);
        Assert.Equal("‹", drawer.Chevron.Text);

        drawer.SetOpen(true, animate: false);
        Layout(host);
        Assert.True(drawer.IsOpen);
        Assert.Equal("›", drawer.Chevron.Text);
        Assert.Equal(PageFavoritesDrawer.BodyWidth, drawer.Body.ActualWidth);
        // Pushed, not covered: the page gives up exactly the body's width.
        Assert.Equal(PageWidth - PageFavoritesDrawer.HandleColumn - PageFavoritesDrawer.BodyWidth, page.ActualWidth);
        Assert.True(Bounds(drawer.Rail, host).Left >= Bounds(page, host).Right);
        // The chip stack is the Home rail's 69, so one chip style fits both drawers.
        Assert.Equal(69, ((FrameworkElement)drawer.FavoritesList.Parent).ActualWidth);
    });

    [Fact]
    public void The_handle_toggles_and_a_navigation_puts_it_away() => WpfRenderHarness.OnStaThread(() =>
    {
        var (host, _, drawer) = Realize();
        drawer.Handle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert.True(drawer.IsOpen);

        drawer.OnNavigated(NavSections.Settings);
        Assert.False(drawer.IsOpen);
        Assert.Equal(Visibility.Visible, drawer.Visibility);
        Layout(host);
        Assert.Equal(0, drawer.Body.ActualWidth);

        drawer.SetOpen(true, animate: false);
        drawer.OnNavigated(NavSections.Home);
        Assert.False(drawer.IsOpen);
        Assert.Equal(Visibility.Collapsed, drawer.Visibility);
    });

    [Fact]
    public void No_effect_sits_on_the_drawer() => WpfRenderHarness.OnStaThread(() =>
    {
        var (_, _, drawer) = Realize();
        drawer.SetOpen(true, animate: false);
        Assert.Null(drawer.Effect);
        Assert.Null(drawer.Handle.Effect);
        Assert.Null(drawer.Rail.Effect);
        Assert.Null(drawer.Body.Effect);
    });

    [Fact]
    public void MainWindow_hosts_it_in_the_page_area_and_feeds_it_the_Home_chips()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root");
        var root = dir!.FullName;
        var xaml = File.ReadAllText(Path.Combine(root, "ConditioningControlPanel", "MainWindow", "MainWindow.xaml"));
        int area = xaml.IndexOf("x:Name=\"PageAreaGrid\"", StringComparison.Ordinal);
        int views = xaml.IndexOf("x:Name=\"PageViewsGrid\"", StringComparison.Ordinal);
        int drawer = xaml.IndexOf("<navrail:PageFavoritesDrawer x:Name=\"PageDrawer\" Grid.Row=\"1\" Grid.Column=\"1\" Visibility=\"Collapsed\"", StringComparison.Ordinal);
        Assert.True(area > 0 && drawer > area && drawer < views, "the drawer is not a column of PageAreaGrid");

        var rail = File.ReadAllText(Path.Combine(root, "ConditioningControlPanel", "MainWindow", "MainWindow.FavoritesRail.cs"));
        int fill = rail.IndexOf("internal void RefreshPageDrawer()", StringComparison.Ordinal);
        Assert.True(fill > 0);
        var body = rail.Substring(fill, 2600);
        // Same builder, same lists: the pins, the order and the right-click menu are Home's.
        Assert.Contains("drawer.FavoritesList.Children.Add(BuildRailChip(", body);
        Assert.Contains("drawer.RecentList.Children.Add(BuildRailChip(", body);
        Assert.Contains("s.RailFavorites", body);
        Assert.Contains("FavoritesRailRule.RecentForDisplay(s.RailRecent, s.RailFavorites", body);

        var chrome = File.ReadAllText(Path.Combine(root, "ConditioningControlPanel", "MainWindow", "MainWindow.SectionChrome.cs"));
        Assert.Contains("PageDrawer?.OnNavigated(section);", chrome);
    }
}
