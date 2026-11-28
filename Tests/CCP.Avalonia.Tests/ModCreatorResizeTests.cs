using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class ModCreatorResizeTests
{
    [Theory]
    [InlineData(5, 400, WindowEdge.West)]
    [InlineData(995, 400, WindowEdge.East)]
    [InlineData(500, 5, WindowEdge.North)]
    [InlineData(500, 795, WindowEdge.South)]
    [InlineData(5, 5, WindowEdge.NorthWest)]
    [InlineData(995, 5, WindowEdge.NorthEast)]
    [InlineData(5, 795, WindowEdge.SouthWest)]
    [InlineData(995, 795, WindowEdge.SouthEast)]
    public void SixPixelBorderMapsAllEdges(double x, double y, WindowEdge edge) =>
        Assert.Equal(edge, ModCreatorWindow.ResizeEdgeAt(new Point(x, y), new Size(1000, 800)));

    [Fact]
    public void InteriorDoesNotResize() =>
        Assert.Null(ModCreatorWindow.ResizeEdgeAt(new Point(6, 6), new Size(1000, 800)));

    [Fact]
    public async Task EdgePressReachesResizeBeforeTitleBarAndInteriorDoesNot() =>
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            LifecycleTestPump.Setup();
            var w = new ModCreatorWindow();
            try
            {
                w.Show(); w.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                w.MouseDown(new Point(500, 2), MouseButton.Left);
                w.MouseUp(new Point(500, 2), MouseButton.Left);
                Assert.Equal(WindowEdge.North, w.LastResizeEdge);
                w.MouseDown(new Point(100, 100), MouseButton.Left);
                w.MouseUp(new Point(100, 100), MouseButton.Left);
                Assert.Equal(WindowEdge.North, w.LastResizeEdge);
                w.CanResize = false;
                w.MouseDown(new Point(2, 300), MouseButton.Left);
                w.MouseUp(new Point(2, 300), MouseButton.Left);
                Assert.Equal(WindowEdge.North, w.LastResizeEdge);
            }
            finally { w.Close(); Dispatcher.UIThread.RunJobs(); }
            return Task.CompletedTask;
        });
}
