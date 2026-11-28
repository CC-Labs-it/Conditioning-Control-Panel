using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using Xunit;

namespace CCP.Avalonia.Tests;

public class SkillTreeScrollBarThemeTests
{
    [Fact]
    public Task SkillTreeHasChunkyThumbAndWheelScrolls() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        AmbientBeatLoopsTests.Setup();
        var tab = new EnhancementsTabView();
        var window = new Window { Width = 800, Height = 650, Content = tab };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var viewer = tab.FindControl<ScrollViewer>("SkillTreeScroller")!;
            Assert.Equal(ScrollBarVisibility.Disabled, viewer.VerticalScrollBarVisibility);
            var bar = viewer.GetVisualDescendants().OfType<ScrollBar>().Single(b => b.Name == "PART_HorizontalScrollBar" && b.TemplatedParent == viewer);
            Assert.Equal(20, bar.Bounds.Height);
            var thumb = bar.GetVisualDescendants().OfType<Thumb>().Single();
            var border = thumb.GetVisualDescendants().OfType<Border>().Single();
            Assert.Equal(new CornerRadius(8), border.CornerRadius);
            Assert.Equal(new Thickness(2), border.Margin);
            Assert.Same(window.FindResource("PinkBrush"), border.Background);
            var point = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), window)!.Value;
            window.MouseMove(point);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Color.Parse("#FF85C4"), ((ISolidColorBrush)border.Background!).Color);
            window.MouseDown(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Color.Parse("#FF4D9E"), ((ISolidColorBrush)border.Background!).Color);
            window.MouseUp(point, MouseButton.Left);
            var wheel = new PointerWheelEventArgs(viewer, new Pointer(1, PointerType.Mouse, true), window, default, 0, default,
                KeyModifiers.None, new Vector(0, -1));
            viewer.RaiseEvent(wheel);
            Assert.True(wheel.Handled);
            Assert.Equal(60, viewer.Offset.X);
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });
}
