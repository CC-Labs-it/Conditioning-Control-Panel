using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class HeadlessFrameTests
{
    [Fact]
    public Task PublishUpdatesPointerGeometryAfterLayoutMoves() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var target = new Border { Width = 40, Height = 40, Background = Brushes.Red };
        var canvas = new Canvas();
        canvas.Children.Add(target);
        var host = new Window { Content = canvas, Width = 240, Height = 100 };
        int enters = 0;
        target.PointerEntered += (_, _) => enters++;
        try
        {
            host.Show();
            HeadlessFrame.Publish(host);
            host.MouseMove(new Point(20, 20));
            Assert.Equal(1, enters);
            host.MouseMove(new Point(220, 80));
            Canvas.SetLeft(target, 120);
            HeadlessFrame.Publish(host);
            Assert.Equal(120, target.Bounds.X);
            host.MouseMove(new Point(20, 20));
            Assert.Equal(1, enters);
            host.MouseMove(new Point(140, 20));
            Assert.Equal(2, enters);
        }
        finally { host.Close(); }
        return Task.CompletedTask;
    });
}
