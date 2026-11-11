using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Possession;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class PossessionGhostTests
{
    [Fact]
    public void CaptureHideRestorePreservesControlState() => AvaloniaTestDispatcher.Run(() =>
    {
        var target = new Border { Width = 50, Height = 30, Opacity = 0.4, IsHitTestVisible = true };
        var layer = new Canvas { IsHitTestVisible = false };
        var window = new Window { Width = 200, Height = 100, Content = new Grid { Children = { target, layer } } };
        window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            using var ghost = Assert.IsType<PossessionGhost>(PossessionGhost.Capture(target, layer));
            Assert.False(ghost.Image.IsHitTestVisible);
            Assert.False(layer.IsHitTestVisible);
            Assert.Single(layer.Children);
            ghost.Hide(true);
            Assert.Equal(0, target.Opacity);
            Assert.False(target.IsHitTestVisible);
            Assert.True(target.IsVisible);
            ghost.Restore();
            Assert.Equal(0.4, target.Opacity);
            Assert.True(target.IsHitTestVisible);
            Assert.True(target.IsVisible);
            Assert.Empty(layer.Children);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void PanicRestoresEveryCapturedControl() => AvaloniaTestDispatcher.Run(() =>
    {
        var first = new Border { Width = 30, Height = 20, Opacity = 0.3 };
        var second = new Border { Width = 30, Height = 20, IsHitTestVisible = false };
        var layer = new Canvas { IsHitTestVisible = false };
        var window = new Window { Width = 200, Height = 100,
            Content = new Grid { Children = { new StackPanel { Children = { first, second } }, layer } } };
        window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            var a = Assert.IsType<PossessionGhost>(PossessionGhost.Capture(first, layer));
            var b = Assert.IsType<PossessionGhost>(PossessionGhost.Capture(second, layer));
            a.Hide(); b.Hide(true);
            PossessionRuntime.UndoAllLive();
            Assert.Equal(0.3, first.Opacity);
            Assert.Equal(1, second.Opacity);
            Assert.False(second.IsHitTestVisible);
            Assert.True(first.IsVisible); Assert.True(second.IsVisible);
            Assert.Empty(layer.Children);
        }
        finally { PossessionRuntime.UndoAllLive(); window.Close(); }
    });
}
