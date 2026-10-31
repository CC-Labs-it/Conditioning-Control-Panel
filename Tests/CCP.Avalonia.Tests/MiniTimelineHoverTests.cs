using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Models.Deeper;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class MiniTimelineHoverTests
{
    [Fact]
    public Task PointerShowsTimeClampsAtBothEdgesAndHidesOnLeave() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        AmbientBeatLoopsTests.Setup();
        var enhancement = new Enhancement { Regions = { new Region { Start = 0, End = 120 } } };
        var window = new EnhancementPlayerWindow(enhancement, null);
        var empty = new EnhancementPlayerWindow(null, null);
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var canvas = window.FindControl<Canvas>("MiniTimelineCanvas")!;
            var hover = window.FindControl<Border>("MiniHoverLabel")!;
            var time = window.FindControl<TextBlock>("TxtMiniHoverTime")!;
            Assert.True(canvas.Bounds.Width > 0);
            Assert.False(hover.IsVisible);
            Assert.Equal(Application.Current!.Resources["Font.Mono"], time.FontFamily);
            Move(window, canvas, canvas.Bounds.Width / 2);
            Assert.True(hover.IsVisible);
            Assert.Equal("1:00", time.Text);
            Move(window, canvas, -100);
            Assert.Equal("0:00", time.Text);
            Assert.Equal(0, hover.Margin.Left);
            Move(window, canvas, canvas.Bounds.Width + 100);
            Assert.Equal("2:00", time.Text);
            Assert.InRange(hover.Margin.Left, 0, canvas.Bounds.Width - 1);
            canvas.RaiseEvent(new PointerEventArgs(InputElement.PointerExitedEvent, canvas,
                new Pointer(1, PointerType.Mouse, true), window, default, 0, PointerPointProperties.None, KeyModifiers.None));
            Assert.False(hover.IsVisible);

            // No loaded enhancement/usable width: even a delivered move cannot show stale text.
            var emptyCanvas = empty.FindControl<Canvas>("MiniTimelineCanvas")!;
            Move(empty, emptyCanvas, 0);
            Assert.False(empty.FindControl<Border>("MiniHoverLabel")!.IsVisible);
        }
        finally { window.Close(); empty.Close(); }
        return Task.CompletedTask;
    });

    private static void Move(Window window, Canvas canvas, double x) =>
        canvas.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, canvas,
            new Pointer(1, PointerType.Mouse, true), window, canvas.TranslatePoint(new Point(x, 1), window) ?? default,
            0, PointerPointProperties.None, KeyModifiers.None));
}
