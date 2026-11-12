using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosHudGraceTests
{
    [Fact]
    public void Live_pointer_replaces_stale_exit_position()
    {
        var panel = new Rect(0, 0, 100, 100);
        var last = new Point(105, 50);
        Assert.True(ChaosHudWindow.CursorInHudGrace(panel, default, new Point(110, 50), last, false));
        Assert.False(ChaosHudWindow.CursorInHudGrace(panel, default, new Point(150, 50), last, false));
    }

    [Fact]
    public void Unavailable_pointer_does_not_poll_a_stale_exit_forever()
    {
        var panel = new Rect(0, 0, 100, 100);
        var last = new Point(105, 50);
        Assert.True(ChaosHudWindow.CursorInHudGrace(panel, default, null, last, true));
        Assert.False(ChaosHudWindow.CursorInHudGrace(panel, default, null, last, false));
    }
    [Fact]
    public void Wayland_trusts_the_query_only_while_an_x_surface_has_the_pointer()
    {
        var panel = new Rect(0, 0, 100, 100);
        var stale = new Point(105, 50);
        // XQueryPointer keeps the last X position after a native Wayland surface takes the pointer.
        Assert.False(ChaosHudWindow.CursorInHudGrace(panel, default, stale, stale, false, true, false));
        // A last position is usable while the window itself is hovered.
        Assert.True(ChaosHudWindow.CursorInHudGrace(panel, default, null, stale, true, true, false));
        // A root child (this window, its tooltip, another X app) makes the position live.
        Assert.True(ChaosHudWindow.CursorInHudGrace(panel, default, stale, stale, false, true, true));
        Assert.False(ChaosHudWindow.CursorInHudGrace(panel, default, new Point(150, 50), stale, true, true, true));
        // Plain X11 is not subject to this.
        Assert.True(ChaosHudWindow.CursorInHudGrace(panel, default, stale, stale, false, false, false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Wayland_does_not_extend_the_window_into_the_halo(bool onRight)
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var oldOverride = ChaosHudWindow.WaylandOverride;
            var oldSide = CoreSettings.Current.ChaosHudOnRight;
            ChaosHudWindow? hud = null;
            try
            {
                CoreSettings.Current.ChaosHudOnRight = onRight;
                double baselineHeight = 0;
                foreach (bool wayland in new[] { false, true })
                {
                    ChaosHudWindow.WaylandOverride = wayland;
                    hud = new ChaosHudWindow(new ChaosRunState(new ChaosRunConfig()));
                    hud.Show();
                    if (!wayland) baselineHeight = hud.Height;
                    Assert.Equal(300, hud.Width);
                    Assert.Equal(baselineHeight, hud.Height);
                    hud.SetPreRunExpanded(true);
                    hud.UpdateLayout();
                    // the panel's layout parent; the panel itself is still mid-slide
                    var root = (Control)hud.FindControl<Border>("Panel")!.Parent!;
                    var at = root.TranslatePoint(new Point(0, 0), hud)!.Value;
                    Assert.Equal(300, hud.Width);
                    Assert.Equal(baselineHeight, hud.Height);
                    Assert.Equal(0, at.X);
                    // just inside the inner halo edge, beside the panel
                    var halo = new Point(onRight ? -1 : 301, 50);
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();   // hit testing reads the last rendered frame
                    hud.MouseMove(halo, RawInputModifiers.None);
                    Dispatcher.UIThread.RunJobs();
                    Assert.False(hud.IsPointerOver);
                    hud.Close();
                    hud = null;
                }
            }
            finally
            {
                hud?.Close();
                ChaosHudWindow.WaylandOverride = oldOverride;
                CoreSettings.Current.ChaosHudOnRight = oldSide;
            }
        });
    }
}
