using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public class ChaosHudGraceTests
{
    [Fact]
    public void GeometryAndUnavailablePointerFallback()
    {
        var panel = new Rect(100, 0, 300, 400);
        var strip = new Rect(0, 0, 100, 400);
        Assert.True(ChaosHudWindow.CursorInGrace(panel, strip, new Point(50, 50), 26));
        Assert.True(ChaosHudWindow.CursorInGrace(panel, strip, new Point(-20, 50), 26));
        Assert.False(ChaosHudWindow.CursorInGrace(panel, strip, new Point(-30, 50), 26));
        Assert.True(ChaosHudWindow.CursorInGrace(panel, strip, new Point(420, 50), 26));
        Assert.False(ChaosHudWindow.CursorInGrace(panel, strip, new Point(430, 50), 26));
        Assert.True(ChaosHudWindow.CursorInGrace(panel, strip, null, 26, true));
        Assert.False(ChaosHudWindow.CursorInGrace(panel, strip, null, 26, false));
    }

    [Fact]
    public async Task CurrentDesktopPointerControlsGraceAndUnavailablePointerDoesNotPollStaleExit()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var hud = new ChaosHudWindow();
            try
            {
                hud.Show();
                hud.UpdateLayout();
                // The headless pointer is not over the window, like a popup taking the hit test.
                hud.ReadPointer = () => hud.PointToScreen(new Point(50, 50));
                Assert.True(hud.CursorInHudGrace());
                hud.ReadPointer = () => hud.PointToScreen(new Point(-100, 50));
                Assert.False(hud.CursorInHudGrace());
                hud.ReadPointer = () => hud.PointToScreen(new Point(hud.Bounds.Width + 100, 50));
                Assert.False(hud.CursorInHudGrace());
                hud.ReadPointer = () => hud.PointToScreen(new Point(50, 50));
                Assert.True(hud.CursorInHudGrace());
                hud.ReadPointer = () => null;
                Assert.False(hud.CursorInHudGrace());
            }
            finally { hud.Close(); }
            return Task.CompletedTask;
        });
    }
}
