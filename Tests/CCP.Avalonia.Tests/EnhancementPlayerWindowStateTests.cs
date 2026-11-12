using Avalonia;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using Xunit;

namespace CCP.Avalonia.Tests;

public class EnhancementPlayerWindowStateTests
{
    [Fact]
    public void PlayerRestoresAndSavesSize() => AvaloniaTestDispatcher.Run(() =>
    {
        var settings = CoreSettings.Current;
        var oldWidth = settings.DeeperPlayerWindowWidth;
        var oldHeight = settings.DeeperPlayerWindowHeight;
        var oldLeft = settings.DeeperPlayerWindowLeft;
        var oldTop = settings.DeeperPlayerWindowTop;
        try
        {
            settings.DeeperPlayerWindowWidth = 1000;
            settings.DeeperPlayerWindowHeight = 700;
            var player = new EnhancementPlayerWindow(null, null);
            Assert.Equal(1000, player.Width);
            Assert.Equal(700, player.Height);
            player.Show();
            player.Width = 1100;
            player.Height = 800;
            player.Close();
            Assert.Equal(1100, settings.DeeperPlayerWindowWidth);
            Assert.Equal(800, settings.DeeperPlayerWindowHeight);
        }
        finally
        {
            settings.DeeperPlayerWindowWidth = oldWidth;
            settings.DeeperPlayerWindowHeight = oldHeight;
            settings.DeeperPlayerWindowLeft = oldLeft;
            settings.DeeperPlayerWindowTop = oldTop;
        }
    });

    [Fact]
    public void WpfPositionUsesSystemScaleBeforeSelectingMonitor()
    {
        var screens = new[] { (new PixelRect(0, 0, 1920, 1080), 1.0), (new PixelRect(1920, 0, 1920, 1080), 1.5) };
        var position = EnhancementPlayerWindow.SelectBoundsPosition(1500, 100, 900, 768, screens)!.Value;
        Assert.Equal(new PixelPoint(3000, 150), EnhancementPlayerWindow.SelectBoundsPosition(2000, 100, 900, 768, screens));
        var secondary = new PixelRect(1920, 0, 1920, 1080);
        Assert.Equal(new PixelPoint(1500, 100), position);
        Assert.False(secondary.Contains(position));
        Assert.True(EnhancementPlayerWindow.BoundsVisible(position, 900, 768, 1, new PixelRect(0, 0, 1920, 1080)));
    }

    [Fact]
    public void PositionRequiresGrabbableVisibleArea()
    {
        var screen = new PixelRect(0, 0, 1920, 1080);
        Assert.True(EnhancementPlayerWindow.BoundsVisible(new PixelPoint(1720, 960), 900, 768, 1, screen));
        Assert.False(EnhancementPlayerWindow.BoundsVisible(new PixelPoint(1721, 960), 900, 768, 1, screen));
        Assert.False(EnhancementPlayerWindow.BoundsVisible(new PixelPoint(1720, 961), 900, 768, 1, screen));
    }
}
