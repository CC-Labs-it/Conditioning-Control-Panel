using System;
using System.Linq;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public class GazeDriftCorrectionTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static (DateTime At, double X, double Y)[] Fixation(double x = 100, double y = 100) =>
        Enumerable.Range(0, 5).Select(i => (Now.AddMilliseconds(-i * 50), x, y)).ToArray();
    private static WebcamCalibrationData Calibration() => new() { MonitorBounds = new MonitorBoundsRecord
        { DeviceName = "screen", X = 1000, Y = 200, Width = 800, Height = 600, DpiScale = 2 } };
    private static (RuntimeOffsetData Offset, bool Persist)? Click(GazeDriftCorrection c, WebcamCalibrationData? cal,
        (DateTime At, double X, double Y)[] samples, double x = 1400, double y = 400,
        bool enabled = true, bool tracking = true, bool consent = true, bool owned = false, DateTime? now = null) =>
        c.TryCorrect(enabled, tracking, consent, owned, cal, samples, x, y, now ?? Now);

    [Fact]
    public void GatesRequireConsentTrackingSettingAndUnownedCalibration()
    {
        var c = new GazeDriftCorrection(); var cal = Calibration(); var gaze = Fixation();
        Assert.Null(Click(c, cal, gaze, enabled: false));
        Assert.Null(Click(c, cal, gaze, tracking: false));
        Assert.Null(Click(c, cal, gaze, consent: false));
        Assert.Null(Click(c, cal, gaze, owned: true));
        Assert.Null(Click(c, null, gaze));
        Assert.NotNull(Click(c, cal, gaze));
    }

    [Fact]
    public void RequiresFreshStableFixation()
    {
        var c = new GazeDriftCorrection(); var cal = Calibration();
        Assert.Null(Click(c, cal, Fixation().Take(4).ToArray()));
        Assert.Null(Click(c, cal, Fixation().Select(s => (s.At.AddSeconds(-1), s.X, s.Y)).ToArray()));
        var moving = Fixation(); moving[0].X = 600;
        Assert.Null(Click(c, cal, moving));
        Assert.Null(Click(c, cal, Fixation(double.NaN)));
        Assert.NotNull(Click(c, cal, Fixation()));
    }

    [Fact]
    public void ConvertsMonitorCoordinatesAndRejectsOutsideOrImplausibleResiduals()
    {
        var c = new GazeDriftCorrection(); var cal = Calibration(); var gaze = Fixation();
        Assert.Null(Click(c, cal, gaze, x: 999));
        Assert.Null(Click(c, cal, gaze, x: 1400, y: 1402));
        Assert.Null(Click(c, cal, gaze, x: 2000));
        Assert.Null(Click(c, cal, gaze, x: 1200));
        var result = Click(c, cal, gaze)!.Value;
        Assert.Equal(15, result.Offset.Dx, 5);
        Assert.Equal(0, result.Offset.Dy, 5);
    }

    [Fact]
    public void ClampsOffsetsThrottlesWritesAndResetsSession()
    {
        var c = new GazeDriftCorrection(); var cal = Calibration();
        cal.RuntimeOffset = new RuntimeOffsetData { Dx = 299, Dy = -299 };
        var first = Click(c, cal, Fixation(), y: 200)!.Value;
        Assert.Equal(300, first.Offset.Dx); Assert.Equal(-300, first.Offset.Dy);
        Assert.True(first.Persist);
        Assert.Null(Click(c, cal, Fixation(), now: Now.AddMilliseconds(100)));
        var gaze = Fixation().Select(s => (s.At.AddSeconds(1), s.X, s.Y)).ToArray();
        Assert.False(Click(c, cal, gaze, now: Now.AddSeconds(1))!.Value.Persist);
        gaze = Fixation().Select(s => (s.At.AddSeconds(31), s.X, s.Y)).ToArray();
        Assert.True(Click(c, cal, gaze, now: Now.AddSeconds(31))!.Value.Persist);
        c.Reset();
        Assert.True(Click(c, cal, gaze, now: Now.AddSeconds(31))!.Value.Persist);
    }
    [Fact]
    public void SettledRepositionRaisesGainOnlyWhilePoseIsFresh()
    {
        var c = new GazeDriftCorrection(); var cal = Calibration();
        for (int i = 0; i < 20; i++) c.Pose(0, 0, Now.AddMilliseconds(i * 50));
        for (int i = 20; i < 40; i++) c.Pose(0.12, 0, Now.AddMilliseconds(i * 50));
        var at = Now.AddMilliseconds(1950);
        var gaze = Fixation().Select(s => (s.At.AddMilliseconds(1950), s.X, s.Y)).ToArray();
        var fast = Click(c, cal, gaze, x: 1700, now: at)!.Value;
        Assert.True(fast.Offset.Dx > 100); // residual 250, rejected at the normal 220 cap
        at = at.AddSeconds(2);
        gaze = Fixation().Select(s => (s.At.AddSeconds(3.95), s.X, s.Y)).ToArray();
        Assert.Null(Click(c, cal, gaze, x: 1700, now: at));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FastModeStopsAfterConvergenceOrNudgeBudget(bool converge)
    {
        var c = new GazeDriftCorrection(); var cal = Calibration();
        for (int i = 0; i < 20; i++) c.Pose(0, 0, Now.AddMilliseconds(i * 50));
        for (int i = 20; i < 40; i++) c.Pose(0.12, 0, Now.AddMilliseconds(i * 50));
        var initial = Fixation().Select(s => (s.At.AddMilliseconds(1950), s.X, s.Y)).ToArray();
        Assert.NotNull(Click(c, cal, initial, x: 1700, now: Now.AddMilliseconds(1950)));
        int count = converge ? 3 : 14;
        for (int i = 0; i < count; i++)
        {
            var at = Now.AddMilliseconds(2450 + i * 500);
            c.Pose(0.12, 0, at);
            var gaze = Fixation().Select(s => (s.At.AddMilliseconds(2450 + i * 500), s.X, s.Y)).ToArray();
            Assert.NotNull(Click(c, cal, gaze, x: converge ? 1240 : 1400, now: at));
        }
        var last = Now.AddMilliseconds(2450 + count * 500);
        c.Pose(0.12, 0, last);
        var samples = Fixation().Select(s => (s.At.AddMilliseconds(2450 + count * 500), s.X, s.Y)).ToArray();
        Assert.Null(Click(c, cal, samples, x: 1700, now: last));
    }

}
