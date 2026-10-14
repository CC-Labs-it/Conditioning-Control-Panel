using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Skia;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class BlinkTrainerResponsiveTests
{
    private static void SetUp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia().WithInterFont()
                .With(ConditioningControlPanel.Avalonia.Platform.AppFonts.Options(systemEmoji: false))
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public void Tab_ResizingCollapsesArt_HoldsAcrossScrollbarWidths_AndRestoresColumns()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            SetUp();
            var tab = new BlinkTrainerTabView();
            var host = new Window { Width = 1200, Height = 1800, Content = tab };
            host.Show();
            try
            {
                var art = tab.FindControl<Border>("SideArt")!;
                var grid = Assert.IsType<Grid>(art.Parent);
                Assert.True(art.IsVisible);
                Assert.Equal(new GridLength(2, GridUnitType.Star), grid.ColumnDefinitions[1].Width);
                Assert.Equal(780, grid.ColumnDefinitions[0].MaxWidth);

                host.Width = 920;
                Dispatcher.UIThread.RunJobs();
                host.UpdateLayout();
                Assert.InRange(grid.Bounds.Width, 800, 859);
                Assert.False(art.IsVisible);
                Assert.Equal(new GridLength(0), grid.ColumnDefinitions[1].Width);
                Assert.Equal(double.PositiveInfinity, grid.ColumnDefinitions[0].MaxWidth);
                Assert.True(tab.FindControl<Border>("BlinkTrainerStageFrame")!.IsVisible);
                Assert.True(tab.FindControl<Grid>("BlinkTrainerGatedZone")!.IsVisible);

                host.Width = 950;
                Dispatcher.UIThread.RunJobs();
                host.UpdateLayout();
                Assert.InRange(grid.Bounds.Width, 860, 907);
                Assert.False(art.IsVisible); // WPF: hold until 860 + 48, avoiding scrollbar flicker.

                host.Width = 1200;
                Dispatcher.UIThread.RunJobs();
                host.UpdateLayout();
                Assert.True(art.IsVisible);
                Assert.Equal(new GridLength(2, GridUnitType.Star), grid.ColumnDefinitions[1].Width);
                Assert.Equal(780, grid.ColumnDefinitions[0].MaxWidth);
            }
            finally { host.Close(); }
        });
    }

    [Fact]
    public void AttachedBehavior_RestoresAuthoredPixelArtWidth_AndEverySettingsCap()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            SetUp();
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,123") };
            grid.ColumnDefinitions[0].MaxWidth = 310;
            grid.ColumnDefinitions[1].MaxWidth = 420;
            var art = new Border();
            Grid.SetColumn(art, 2);
            grid.Children.Add(art);
            var settings = new Border();
            Grid.SetColumnSpan(settings, 3);
            grid.Children.Add(settings);
            AdaptiveSideArt.SetCollapseBelow(grid, 860);
            var host = new Window { Width = 859, Height = 500, Content = grid };
            host.Show();
            try
            {
                Assert.False(art.IsVisible);
                Assert.True(settings.IsVisible); // A spanning row belongs to the settings column.
                Assert.Equal(double.PositiveInfinity, grid.ColumnDefinitions[0].MaxWidth);
                Assert.Equal(double.PositiveInfinity, grid.ColumnDefinitions[1].MaxWidth);
                host.Width = 908;
                Dispatcher.UIThread.RunJobs();
                host.UpdateLayout();
                Assert.True(art.IsVisible);
                Assert.Equal(new GridLength(123), grid.ColumnDefinitions[2].Width);
                Assert.Equal(310, grid.ColumnDefinitions[0].MaxWidth);
                Assert.Equal(420, grid.ColumnDefinitions[1].MaxWidth);
            }
            finally { host.Close(); }
        });
    }
}
