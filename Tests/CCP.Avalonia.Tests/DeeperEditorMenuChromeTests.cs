using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Models.Deeper;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class DeeperEditorMenuChromeTests
{
    [Fact]
    public Task TimelineMenuUsesDeeperSurface() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var editor = new DeeperEditorWindow(new Enhancement(), null);
        try
        {
            editor.Show();
            var canvas = editor.FindControl<Canvas>("TimelineCanvas")!;
            var menu = canvas.ContextMenu!;
            menu.Open(canvas);
            Dispatcher.UIThread.RunJobs();
            Assert.True(menu.IsOpen);
            var border = menu.GetVisualDescendants().OfType<Border>().First();
            Assert.Equal(new CornerRadius(8), border.CornerRadius);
            Assert.Equal(new Thickness(2), border.BorderThickness);
            Assert.Equal(new Thickness(4), border.Padding);
            Assert.Equal(((ISolidColorBrush)editor.FindResource("DarkerBgBrush")!).Color, ((ISolidColorBrush)border.Background!).Color);
            Assert.Equal(((ISolidColorBrush)editor.FindResource("DeeperAccentBrush")!).Color, ((ISolidColorBrush)border.BorderBrush!).Color);
            menu.Close();
        }
        finally { editor.ForceClose(); }
        return Task.CompletedTask;
    });
    [Fact]
    public Task HeroEffectMenuUsesDeeperSurfaceAndRows() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var editor = new DeeperEditorWindow(new Enhancement(), null);
        try
        {
            editor.Show();
            var button = editor.FindControl<Button>("BtnAddEffectHero")!;
            var flyout = (MenuFlyout)button.Flyout!;
            flyout.ShowAt(button);
            Dispatcher.UIThread.RunJobs();
            Assert.True(flyout.IsOpen);
            var row = editor.FindControl<MenuItem>("HeroEffectHaptic")!;
            var presenter = row.GetVisualAncestors().OfType<MenuFlyoutPresenter>().First();
            var border = presenter.GetVisualDescendants().OfType<Border>().First(b => b.Name == "LayoutRoot");
            Assert.Equal(new CornerRadius(8), border.CornerRadius);
            Assert.Equal(new Thickness(2), border.BorderThickness);
            Assert.Equal(new Thickness(4), border.Padding);
            Assert.Equal(((ISolidColorBrush)editor.FindResource("DarkerBgBrush")!).Color, ((ISolidColorBrush)border.Background!).Color);
            Assert.Equal(((ISolidColorBrush)editor.FindResource("DeeperAccentBrush")!).Color, ((ISolidColorBrush)border.BorderBrush!).Color);
            Assert.Equal(new CornerRadius(0), row.CornerRadius);
            Assert.Equal(new Thickness(0), row.Margin);
            Assert.Equal(new Thickness(10, 6), row.Padding);
            Assert.Equal(12, row.FontSize);
            row.IsSelected = true;
            Dispatcher.UIThread.RunJobs();
            var rowBorder = row.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_LayoutRoot");
            Assert.Equal(((ISolidColorBrush)editor.FindResource("DeeperMenuItemHoverBgBrush")!).Color, ((ISolidColorBrush)rowBorder.Background!).Color);
            row.IsSelected = false;
            row.IsEnabled = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, row.Opacity);
            var header = row.GetVisualDescendants().OfType<global::Avalonia.Controls.Presenters.ContentPresenter>().First(b => b.Name == "PART_HeaderPresenter");
            Assert.Equal(Color.Parse("#666666"), ((ISolidColorBrush)header.Foreground!).Color);
            flyout.Hide();
        }
        finally { editor.ForceClose(); }
        return Task.CompletedTask;
    });
}
