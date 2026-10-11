using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Flash;
using ConditioningControlPanel.Services.Video.Floating;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// One floating video window (7.1.6 juice pass): it is thrown and bounces by the flash images'
/// rules, it is never left off screen, its border eases to a light when it carries the sound,
/// and the frame never takes focus.
/// </summary>
public class FloatingVideoFrameTests
{
    // A 400 x 240 window in a 1920 x 1040 work area that starts at (0, 0).
    private static FloatingVideoMotion Window(double x = 600, double y = 300)
        => new(x, y, 400, 240, 0, 0, 1920, 1040);

    private static void Throw(FloatingVideoMotion m, double vxPerMs, double vyPerMs, MotionLevel level = MotionLevel.Full)
    {
        double px = m.X + 50, py = m.Y + 50;
        m.Press(px, py, 0);
        m.Move(px + vxPerMs * 100, py + vyPerMs * 100, 100);
        m.Move(px + vxPerMs * 116, py + vyPerMs * 116, 116);
        m.Step(0.016);
        m.Release(120, level);
    }

    [Fact]
    public void A_press_that_does_not_move_is_a_touch()
    {
        var m = Window();
        m.Press(650, 350, 0);
        Assert.True(m.Held);
        Assert.Equal(FlashDragOutcome.Tap, m.Release(90, MotionLevel.Full));
        Assert.False(m.Active);
        Assert.Equal((600.0, 300.0), (m.X, m.Y));
    }

    [Fact]
    public void A_held_window_follows_the_hand_from_where_it_was_grabbed()
    {
        var m = Window();
        m.Press(650, 350, 0);
        m.Move(750, 420, 400);
        Assert.True(m.Step(0.016));
        Assert.Equal((700.0, 370.0), (m.X, m.Y));
    }

    [Fact]
    public void A_slow_release_places_it_and_nothing_flies()
    {
        var m = Window();
        m.Press(650, 350, 0);
        m.Move(700, 350, 300);
        m.Move(701, 350, 600);
        m.Step(0.016);
        Assert.Equal(FlashDragOutcome.Place, m.Release(900, MotionLevel.Full));
        Assert.False(m.Active);
        Assert.False(m.Step(0.033));
    }

    [Fact]
    public void A_throw_keeps_moving_bounces_and_comes_to_rest_inside()
    {
        var m = Window();
        Throw(m, 3.0, 1.2);
        Assert.True(m.Flying);

        bool sawLeftward = false;
        int steps = 0;
        while (m.Active && steps++ < 2000)
        {
            m.Step(1.0 / 30.0);
            if (m.Vx < 0) sawLeftward = true;
            Assert.InRange(m.X, 0, 1920 - 400);
            Assert.InRange(m.Y, 0, 1040 - 240);
        }
        Assert.True(sawLeftward, "a rightward throw comes back off the right edge");
        Assert.False(m.Active);                // friction ends every flight
        Assert.True(steps < 2000);
        Assert.Equal((0.0, 0.0), (m.Vx, m.Vy));
    }

    [Fact]
    public void A_bounce_costs_what_a_flash_bounce_costs()
    {
        var m = Window(x: 1400);
        Throw(m, 4.0, 0.0);
        double before = Math.Abs(m.Vx);
        double dt = 1.0 / 30.0;
        while (m.Vx > 0) { before = m.Vx; m.Step(dt); }
        var expected = before * FlashDrag.BounceKeep * Math.Pow(FlashDrag.FrictionKeepPerSec, dt);
        Assert.Equal(expected, -m.Vx, 6);
    }

    [Theory]
    [InlineData(MotionLevel.Off, FlashDragOutcome.Place)]
    [InlineData(MotionLevel.Reduced, FlashDragOutcome.Fling)]
    public void Motion_level_off_never_throws(MotionLevel level, FlashDragOutcome expected)
    {
        var m = Window();
        double px = m.X + 50, py = m.Y + 50;
        m.Press(px, py, 0);
        m.Move(px + 300, py, 100);
        m.Move(px + 348, py, 116);
        m.Step(0.016);
        Assert.Equal(expected, m.Release(120, level));
    }

    [Fact]
    public void A_held_window_cannot_be_dragged_off_screen()
    {
        var m = Window();
        m.Press(650, 350, 0);
        m.Move(-4000, 9000, 300);
        m.Step(0.016);
        Assert.Equal((0.0, 1040.0 - 240.0), (m.X, m.Y));
    }

    [Fact]
    public void A_window_placed_outside_is_pulled_back_in()
    {
        var m = new FloatingVideoMotion(5000, -700, 400, 240, 100, 50, 1920, 1040);
        Assert.Equal((100.0 + 1920 - 400, 50.0), (m.X, m.Y));
        m.Resync(-300, 4000, 400, 240);
        Assert.Equal((100.0, 50.0 + 1040 - 240), (m.X, m.Y));
    }

    [Fact]
    public void A_window_larger_than_the_area_parks_at_its_corner()
    {
        Assert.Equal(10, FloatingVideoMotion.Clamp(500, 3000, 10, 1920));
        var m = new FloatingVideoMotion(500, 500, 3000, 3000, 10, 20, 1920, 1040);
        Assert.Equal((10.0, 20.0), (m.X, m.Y));
    }

    [Fact]
    public void A_press_catches_a_window_in_flight_and_drop_lets_go()
    {
        var m = Window();
        Throw(m, 3.0, 0.5);
        m.Step(0.033);
        m.Press(m.X + 10, m.Y + 10, 500);
        Assert.True(m.Held);
        Assert.False(m.Flying);
        m.Drop();
        Assert.False(m.Active);
        Assert.False(m.Step(0.033));
    }

    // ---- the light ----

    [Fact]
    public void The_light_eases_in_and_out_and_never_overshoots()
    {
        double lit = 0;
        lit = FloatingFrameLook.Approach(lit, true, FloatingFrameLook.LightInSeconds / 2);
        Assert.InRange(lit, 0.49, 0.51);
        lit = FloatingFrameLook.Approach(lit, true, 5);
        Assert.Equal(1.0, lit);
        lit = FloatingFrameLook.Approach(lit, false, FloatingFrameLook.LightOutSeconds / 4);
        Assert.InRange(lit, 0.74, 0.76);
        Assert.Equal(0.0, FloatingFrameLook.Approach(lit, false, 5));
    }

    [Fact]
    public void A_lit_border_is_the_accent_and_a_quiet_one_is_clearly_dimmer()
    {
        var quiet = FloatingFrameLook.Border(0xFF, 0x69, 0xB4, 0, 1);   // breath never shows on a quiet one
        var lit = FloatingFrameLook.Border(0xFF, 0x69, 0xB4, 1, 0);
        var peak = FloatingFrameLook.Border(0xFF, 0x69, 0xB4, 1, 1);
        Assert.Equal(((byte)0xFF, (byte)0x69, (byte)0xB4), lit);
        Assert.Equal(quiet, FloatingFrameLook.Border(0xFF, 0x69, 0xB4, 0, 0));
        Assert.True(quiet.R + quiet.G + quiet.B < (lit.R + lit.G + lit.B) * 0.6);
        Assert.True(peak.G > lit.G && peak.B > lit.B);
    }

    [Fact]
    public void Breathing_follows_the_motion_level()
    {
        var top = FloatingFrameLook.BreathSeconds / 2;
        Assert.Equal(0, FloatingFrameLook.Breath(0, MotionLevel.Full), 6);
        Assert.Equal(1, FloatingFrameLook.Breath(top, MotionLevel.Full), 6);
        Assert.Equal(0.5, FloatingFrameLook.Breath(top, MotionLevel.Reduced), 6);
        Assert.Equal(0, FloatingFrameLook.Breath(top, MotionLevel.Off), 6);
    }

    // ---- source pins ----

    /// <summary>Nothing in the frame may take focus from the player's app: the window is stamped
    /// no-activate, a press answers MA_NOACTIVATE, every move says SWP_NOACTIVATE, and no code
    /// under Floating/ ever activates, focuses or foregrounds anything.</summary>
    [Fact]
    public void The_frame_never_activates_the_window()
    {
        var dir = Path.Combine(RepoRoot(), "ConditioningControlPanel", "Services", "Video", "Floating");
        var frame = File.ReadAllText(Path.Combine(dir, "FloatingVideoFrame.cs"));
        Assert.Contains("ex | WS_EX_NOACTIVATE", frame);
        Assert.Contains("return (IntPtr)MA_NOACTIVATE;", frame);
        var moves = frame.Split("SetWindowPos(_hwnd").Skip(1).ToArray();
        Assert.NotEmpty(moves);
        Assert.All(moves, m => Assert.Contains("SWP_NOACTIVATE", m.Substring(0, Math.Min(260, m.Length))));

        foreach (var file in Directory.GetFiles(dir, "*.cs"))
        {
            var src = File.ReadAllText(file);
            foreach (var banned in new[] { ".Activate(", ".Focus(", "SetForegroundWindow", "SetActiveWindow", "SetFocus(", "BringWindowToTop", "SetCapture(" })
                Assert.DoesNotContain(banned, src);
        }
    }

    /// <summary>The hook listens and never swallows (the page keeps its click), a frame is one
    /// mover (the old caption drag is refused), and Detach lets go of everything.</summary>
    [Fact]
    public void The_frame_listens_without_swallowing_and_detach_lets_go()
    {
        var dir = Path.Combine(RepoRoot(), "ConditioningControlPanel", "Services", "Video", "Floating");
        var pointer = File.ReadAllText(Path.Combine(dir, "FloatingVideoPointer.cs"));
        var down = pointer.Substring(pointer.IndexOf("private static bool OnDown", StringComparison.Ordinal));
        down = down.Substring(0, down.IndexOf("private static void OnMove", StringComparison.Ordinal));
        Assert.DoesNotContain("return true", down);

        var frame = File.ReadAllText(Path.Combine(dir, "FloatingVideoFrame.cs"));
        Assert.Contains("case WM_NCLBUTTONDOWN when wParam == (IntPtr)HTCAPTION:", frame);
        var detach = frame.Substring(frame.IndexOf("public void Detach()", StringComparison.Ordinal));
        detach = detach.Substring(0, detach.IndexOf("// ---- window ----", StringComparison.Ordinal));
        Assert.Contains("if (_detached) return;", detach);
        Assert.Contains("_clock.Stop();", detach);
        Assert.Contains("FloatingVideoPointer.Unregister(this);", detach);
        Assert.Contains("RemoveHook(_wndProc)", detach);
        Assert.DoesNotContain("AllowsTransparency = true", frame);
    }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !d.GetDirectories("ConditioningControlPanel").Any(x => File.Exists(Path.Combine(x.FullName, "ConditioningControlPanel.csproj"))))
            d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
