using Avalonia;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;
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
        var oldHint = settings.DeeperPlayerWindowPositionHint;
        try
        {
            settings.DeeperPlayerWindowPositionHint = null;
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
            Assert.NotNull(settings.DeeperPlayerWindowPositionHint);
            Assert.Equal(settings.DeeperPlayerWindowLeft, settings.DeeperPlayerWindowPositionHint![0]);
            Assert.Equal(settings.DeeperPlayerWindowTop, settings.DeeperPlayerWindowPositionHint[1]);
        }
        finally
        {
            settings.DeeperPlayerWindowWidth = oldWidth;
            settings.DeeperPlayerWindowHeight = oldHeight;
            settings.DeeperPlayerWindowLeft = oldLeft;
            settings.DeeperPlayerWindowTop = oldTop;
            settings.DeeperPlayerWindowPositionHint = oldHint;
        }
    });

    [Fact]
    public void ClosingTwiceOrBeforeShowingDoesNotOverwriteSavedBounds() => AvaloniaTestDispatcher.Run(() =>
    {
        var settings = CoreSettings.Current;
        var old = (settings.DeeperPlayerWindowLeft, settings.DeeperPlayerWindowTop,
            settings.DeeperPlayerWindowWidth, settings.DeeperPlayerWindowHeight, settings.DeeperPlayerWindowPositionHint);
        try
        {
            var player = new EnhancementPlayerWindow(null, null);
            player.Show();
            player.Width = 1100;
            player.Height = 800;
            player.Close();
            Assert.Equal(1100, settings.DeeperPlayerWindowWidth);
            Assert.Equal(800, settings.DeeperPlayerWindowHeight);
            settings.DeeperPlayerWindowWidth = 1200;
            player.Close();
            Assert.Equal(1200, settings.DeeperPlayerWindowWidth);
            var unopened = new EnhancementPlayerWindow(null, null);
            unopened.Close();
            Assert.Equal(1200, settings.DeeperPlayerWindowWidth);
        }
        finally
        {
            settings.DeeperPlayerWindowLeft = old.DeeperPlayerWindowLeft;
            settings.DeeperPlayerWindowTop = old.DeeperPlayerWindowTop;
            settings.DeeperPlayerWindowWidth = old.DeeperPlayerWindowWidth;
            settings.DeeperPlayerWindowHeight = old.DeeperPlayerWindowHeight;
            settings.DeeperPlayerWindowPositionHint = old.DeeperPlayerWindowPositionHint;
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
    public void MixedDpiPositionRoundTripsAndStaleHintDoesNotOverrideWpf()
    {
        var screens = new[] { (new PixelRect(0, 0, 1920, 1080), 1.0), (new PixelRect(1920, 0, 1920, 1080), 1.5) };
        var settings = new AppSettings();
        EnhancementPlayerWindow.SaveWindowPosition(settings, new PixelPoint(2000, 150), 1.5);
        var restored = JsonConvert.DeserializeObject<AppSettings>(JsonConvert.SerializeObject(settings))!;
        Assert.Equal(2000 / 1.5, restored.DeeperPlayerWindowLeft);
        Assert.Equal(new PixelPoint(2000, 150), EnhancementPlayerWindow.SelectBoundsPosition(
            restored.DeeperPlayerWindowLeft, restored.DeeperPlayerWindowTop, 900, 768, screens, restored.DeeperPlayerWindowPositionHint));
        var primaryOnly = new[] { screens[0] };
        Assert.Equal(new PixelPoint(1333, 100), EnhancementPlayerWindow.SelectBoundsPosition(
            restored.DeeperPlayerWindowLeft, restored.DeeperPlayerWindowTop, 900, 768, primaryOnly, restored.DeeperPlayerWindowPositionHint));
        restored.DeeperPlayerWindowLeft = 1500; // WPF changed the saved position.
        Assert.Equal(new PixelPoint(1500, 100), EnhancementPlayerWindow.SelectBoundsPosition(
            restored.DeeperPlayerWindowLeft, restored.DeeperPlayerWindowTop, 900, 768, screens, restored.DeeperPlayerWindowPositionHint));
        Assert.Null(EnhancementPlayerWindow.SelectBoundsPosition(4000, 100, 900, 768, screens, new[] { 4000d, 100d, 1.5 }));
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
