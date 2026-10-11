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

    // ---- speed ----

    private static (double Travel, double Peak, bool Rested) Flight(double speed, double vxPerMs = 1.5)
    {
        // Wide bounds so no wall is in the way: travel and peak are the throw's own.
        var m = new FloatingVideoMotion(1000, 300, 400, 240, 0, 0, 100000, 1040) { Speed = speed };
        Throw(m, vxPerMs, 0);
        double x0 = m.X, peak = Math.Abs(m.Vx);
        int steps = 0;
        while (m.Active && steps++ < 4000) { m.Step(1.0 / 30.0); peak = Math.Max(peak, Math.Abs(m.Vx)); }
        return (m.X - x0, peak, !m.Active);
    }

    [Fact]
    public void A_slower_speed_throws_proportionally_slower_and_still_comes_to_rest()
    {
        var full = Flight(1.0);
        var lazy = Flight(0.35);
        Assert.Equal(1500.0, full.Peak, 6);                 // 1.5 px/ms, the flash throw untouched
        Assert.Equal(full.Peak * 0.35, lazy.Peak, 6);
        Assert.InRange(lazy.Travel / full.Travel, 0.30, 0.37);   // rest comes a touch sooner
        Assert.True(full.Rested && lazy.Rested);
        Assert.True(lazy.Travel > 0);
    }

    [Fact]
    public void A_wild_flick_is_capped_at_the_top_speed_for_its_speed()
    {
        Assert.Equal(FloatingVideoMotion.TopSpeedPxPerSec, Flight(1.0, vxPerMs: 20).Peak, 6);
        Assert.Equal(FloatingVideoMotion.TopSpeedPxPerSec * 0.35, Flight(0.35, vxPerMs: 20).Peak, 6);

        var m = Window();
        m.Speed = 0.35;
        Throw(m, 20, 0, MotionLevel.Reduced);               // reduced motion halves the cap as well
        Assert.Equal(FloatingVideoMotion.TopSpeedPxPerSec * 0.35 * 0.5, m.Vx, 6);
    }

    [Fact]
    public void A_fast_drag_is_thrown_the_way_the_hand_went_not_the_way_its_last_twitch_did()
    {
        // A 1 kHz mouse dragged hard to the right; the last sample, a fraction of a millisecond
        // before the button comes up, twitches 2 px back. Read off the last two samples that is a
        // full-speed throw to the LEFT (owner, 2026-10-12: "instantly bounce" far from any edge).
        var m = Window();
        m.Press(650, 350, 0);
        double x = 650;
        for (int ms = 1; ms <= 60; ms++) { x += 3; m.Move(x, 350, ms); }
        m.Move(x - 2, 350, 60.05);
        Assert.Equal(FlashDragOutcome.Fling, m.Release(60.1, MotionLevel.Full));
        Assert.True(m.Vx > 0, $"thrown backwards: {m.Vx}");
        Assert.Equal(0, m.Vy, 6);
    }

    [Fact]
    public void A_hand_that_stopped_before_letting_go_places_the_window()
    {
        var m = Window();
        m.Press(650, 350, 0);
        for (int ms = 1; ms <= 40; ms++) m.Move(650 + ms * 3, 350, ms);
        Assert.Equal(FlashDragOutcome.Place, m.Release(400, MotionLevel.Full));
        Assert.False(m.Active);
    }

    [Fact]
    public void Speed_never_touches_the_drag_under_the_hand()
    {
        var m = Window();
        m.Speed = 0.1;
        m.Press(650, 350, 0);
        m.Move(750, 420, 400);
        m.Step(0.016);
        Assert.Equal((700.0, 370.0), (m.X, m.Y));
    }

    [Fact]
    public void Speed_is_clamped_and_a_window_in_flight_follows_a_change()
    {
        var m = Window();
        m.Speed = 7;
        Assert.Equal(FloatingVideoMotion.MaxSpeed, m.Speed);
        m.Speed = -1;
        Assert.Equal(FloatingVideoMotion.MinSpeed, m.Speed);
        m.Speed = double.NaN;
        Assert.Equal(1.0, m.Speed);

        Throw(m, 2.0, 1.0);
        double vx = m.Vx, vy = m.Vy;
        m.Speed = 0.5;
        Assert.Equal((vx * 0.5, vy * 0.5), (m.Vx, m.Vy));
        m.Speed = 1.0;
        Assert.Equal(vx, m.Vx, 6);
    }

    // ---- monitors ----

    // Two monitors side by side: A = 1920 x 1040 at (0, 0), B = 2560 x 1400 at (1920, 0).
    private static (double X, double Y, double W, double H)? TwoMonitors(double x, double y)
        => x < 1920 ? (0, 0, 1920, 1040) : (1920, 0, 2560, 1400);

    private static FloatingVideoMotion OnTwoMonitors(double x = 600, double y = 300)
    {
        var m = new FloatingVideoMotion(x, y, 400, 240, 0, 0, 1920, 1040) { MonitorAt = TwoMonitors };
        m.SetDesktop(0, 0, 1920 + 2560, 1400);
        return m;
    }

    [Fact]
    public void A_held_window_crosses_to_another_monitor_and_stops_only_at_the_desktop_edge()
    {
        var m = OnTwoMonitors();
        m.Press(650, 350, 0);
        m.Move(3050, 1150, 400);
        m.Step(0.016);
        Assert.Equal((3000.0, 1100.0), (m.X, m.Y));        // on B, past A's right and bottom edges
        m.Move(90000, 90000, 800);
        m.Step(0.016);
        Assert.Equal((1920.0 + 2560 - 400, 1400.0 - 240), (m.X, m.Y));
    }

    [Fact]
    public void Let_go_on_another_monitor_it_bounces_inside_that_work_area()
    {
        var m = OnTwoMonitors();
        m.Press(650, 350, 0);
        m.Move(2900, 700, 400);
        m.Move(2948, 700, 416);                             // 3 px/ms to the right, on B
        m.Step(0.016);
        Assert.Equal(FlashDragOutcome.Fling, m.Release(420, MotionLevel.Full));
        Assert.Equal((1920.0, 0.0, 2560.0, 1400.0), m.Bounds);

        bool cameBack = false;
        int steps = 0;
        while (m.Active && steps++ < 2000)
        {
            m.Step(1.0 / 30.0);
            if (m.Vx < 0) cameBack = true;
            Assert.InRange(m.X, 1920, 1920 + 2560 - 400);   // never back onto A
            Assert.InRange(m.Y, 0, 1400 - 240);
        }
        Assert.True(cameBack);
        Assert.False(m.Active);
    }

    [Fact]
    public void A_tap_or_a_placement_also_takes_the_monitor_under_the_centre()
    {
        var m = OnTwoMonitors();
        m.Press(650, 350, 0);
        m.Move(1800, 350, 300);                             // window at x 1750..2150: centre on B
        m.Move(1801, 350, 700);
        m.Step(0.016);
        Assert.Equal(FlashDragOutcome.Place, m.Release(1000, MotionLevel.Full));
        Assert.Equal(1920.0, m.Bounds.X);
        Assert.Equal(1920.0, m.X);                          // pulled whole onto B, not left straddling

        m.Press(m.X + 10, m.Y + 10, 2000);
        m.Move(1700, 400, 2300);                            // centre back on A
        m.Step(0.016);
        m.Drop();                                           // a lost button-up lands it the same way
        Assert.Equal((0.0, 1920.0), (m.Bounds.X, m.Bounds.W));
        Assert.Equal(1920.0 - 400, m.X);
    }

    [Fact]
    public void A_window_left_outside_every_work_area_is_pulled_back()
    {
        var m = OnTwoMonitors();
        m.Press(650, 350, 0);
        m.Move(3050, 1150, 400);
        m.Step(0.016);
        m.Release(900, MotionLevel.Full);
        Assert.Equal(1920.0, m.Bounds.X);

        // B is unplugged: only A answers now.
        m.MonitorAt = (_, _) => (0, 0, 1920, 1040);
        m.Rehome();
        Assert.Equal((1920.0 - 400, 1040.0 - 240), (m.X, m.Y));

        // The frame's later syncs (a press, a display change) take the monitor the window is on.
        m.MonitorAt = TwoMonitors;
        m.Resync(2500, 1300, 400, 240, rehome: true);
        Assert.Equal((2500.0, 1400.0 - 240), (m.X, m.Y));

        // No monitor answer at all keeps the old bounds and still clamps.
        m.MonitorAt = (_, _) => null;
        m.Resync(-900, -900, 400, 240, rehome: true);
        Assert.Equal((1920.0, 0.0), (m.X, m.Y));
    }

    [Fact]
    public void A_window_that_changes_size_in_the_hand_stays_under_the_pointer()
    {
        var m = OnTwoMonitors();
        m.Press(900, 420, 0);                               // grabbed at 3/4 across, 1/2 down
        m.Move(2500, 600, 300);
        m.Step(0.016);
        m.Resize(600, 360);                                 // landed on a 150% monitor
        Assert.Equal((600.0, 360.0), (m.W, m.H));
        Assert.Equal((2500.0 - 450, 600.0 - 180), (m.X, m.Y));
        m.Move(2600, 650, 400);
        m.Step(0.016);
        Assert.Equal((2600.0 - 450, 650.0 - 180), (m.X, m.Y));
    }

    /// <summary>The frame feeds the seam from Win32 and listens for the monitors changing.</summary>
    [Fact]
    public void The_frame_wires_speed_monitors_and_display_changes()
    {
        var dir = Path.Combine(RepoRoot(), "ConditioningControlPanel", "Services", "Video", "Floating");
        var frame = File.ReadAllText(Path.Combine(dir, "FloatingVideoFrame.cs"));
        Assert.Contains("MonitorAt = WorkAreaAt", frame);
        Assert.Contains("MONITOR_DEFAULTTONEAREST", frame);
        Assert.Contains("rcWork", frame);
        Assert.Contains("SM_CXVIRTUALSCREEN", frame);
        Assert.Contains("case WM_DISPLAYCHANGE:", frame);
        Assert.Contains("_motion.Resize(", frame);
        Assert.Contains("if (_motion != null) _motion.Speed = _speed;", frame);
        Assert.DoesNotContain("public double MotionSpeed { get; set; }", frame);
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
