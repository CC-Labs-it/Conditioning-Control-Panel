using ConditioningControlPanel.AvatarTubeLayout;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Chaster;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The big "+3:00": it lands where the time was earned, stacks instead of overlapping, stays on
/// the monitor, and the motion table is the only difference between the three motion levels.
/// </summary>
public class BookedPopLayoutTests
{
    // ---- origin -----------------------------------------------------------

    [Fact]
    public void TheCauseWins_OverTheCursor()
    {
        Assert.Equal(new ScreenPoint(10, 20), BookedPopLayout.ResolveOrigin(new ScreenPoint(10, 20), new ScreenPoint(500, 500)));
    }

    [Fact]
    public void NoCause_FallsBackToTheCursor()
    {
        Assert.Equal(new ScreenPoint(500, 500), BookedPopLayout.ResolveOrigin(null, new ScreenPoint(500, 500)));
    }

    [Fact]
    public void NeitherKnown_MeansTheRail()
    {
        Assert.Null(BookedPopLayout.ResolveOrigin(null, null));
    }

    // ---- motion -----------------------------------------------------------

    [Fact]
    public void Full_PunchesInRisesFadesAndSparks()
    {
        var p = BookedPopLayout.For(MotionLevel.Full);
        Assert.InRange(p.FontDip, 48, 64);
        Assert.True(p.RiseDip > 0);
        Assert.True(p.ScaleInMs > 0 && p.Overshoot > 0);
        Assert.InRange(p.TotalMs, 1200, 1600);
        Assert.True(p.FadeMs > 0 && p.FadeMs < p.TotalMs);
        Assert.True(p.Particles > 0);
    }

    [Fact]
    public void Reduced_HasNoMotion_ButStillShowsTheNumber()
    {
        var p = BookedPopLayout.For(MotionLevel.Reduced);
        Assert.Equal(0, p.RiseDip);
        Assert.Equal(0, p.ScaleInMs);
        Assert.Equal(0, p.Particles);
        Assert.InRange(p.FontDip, 48, 64);
        Assert.True(p.TotalMs >= 1000);
    }

    [Fact]
    public void Off_IsAStaticNumberForAboutASecond()
    {
        var p = BookedPopLayout.For(MotionLevel.Off);
        Assert.Equal(0, p.RiseDip);
        Assert.Equal(0, p.ScaleInMs);
        Assert.Equal(0, p.FadeMs);
        Assert.Equal(0, p.Particles);
        Assert.InRange(p.TotalMs, 1000, 1400);
    }

    // ---- stacking ---------------------------------------------------------

    [Fact]
    public void FirstPop_TakesTheBottomRung()
    {
        Assert.Equal(0, BookedPopLayout.PickSlot(new BookedPopLayout.LivePop[0], new ScreenPoint(100, 100), 0));
    }

    [Fact]
    public void ANearbyLivePop_PushesTheNextOneUp()
    {
        var live = new[] { new BookedPopLayout.LivePop(new ScreenPoint(100, 100), 0, 1500, 0) };
        Assert.Equal(1, BookedPopLayout.PickSlot(live, new ScreenPoint(130, 110), 300));
    }

    [Fact]
    public void AFarAwayPop_DoesNotStack()
    {
        var live = new[] { new BookedPopLayout.LivePop(new ScreenPoint(100, 100), 0, 1500, 0) };
        Assert.Equal(0, BookedPopLayout.PickSlot(live, new ScreenPoint(1400, 800), 300));
    }

    [Fact]
    public void AnExpiredPop_FreesItsRung()
    {
        var live = new[] { new BookedPopLayout.LivePop(new ScreenPoint(100, 100), 0, 1500, 0) };
        Assert.Equal(0, BookedPopLayout.PickSlot(live, new ScreenPoint(100, 100), 1500));
    }

    [Fact]
    public void AFreedLowerRung_IsReusedFirst()
    {
        var live = new[] { new BookedPopLayout.LivePop(new ScreenPoint(100, 100), 0, 1500, 1) };
        Assert.Equal(0, BookedPopLayout.PickSlot(live, new ScreenPoint(100, 100), 100));
    }

    [Fact]
    public void ALongBurst_ReusesTheTopRung()
    {
        var live = new BookedPopLayout.LivePop[BookedPopLayout.MaxSlots];
        for (var i = 0; i < live.Length; i++) live[i] = new BookedPopLayout.LivePop(new ScreenPoint(100, 100), 0, 1500, i);
        Assert.Equal(BookedPopLayout.MaxSlots - 1, BookedPopLayout.PickSlot(live, new ScreenPoint(100, 100), 100));
    }

    // ---- placement --------------------------------------------------------

    private static ScreenPoint FigureCentre(ScreenPoint topLeft, double scale) => new(
        topLeft.X + BookedPopLayout.WindowWidthDip * scale / 2,
        topLeft.Y + BookedPopLayout.WindowHeightDip * scale * BookedPopLayout.FigureAnchorY);

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void TheFigureIsCentredOnTheOrigin_AtEveryScale(double scale)
    {
        var origin = new ScreenPoint(1000, 600);
        var c = FigureCentre(BookedPopLayout.TopLeftPx(origin, 0, scale, new Box(0, 0, 2560, 1440)), scale);
        Assert.InRange(c.X, origin.X - 1, origin.X + 1);
        Assert.InRange(c.Y, origin.Y - 1, origin.Y + 1);
    }

    [Fact]
    public void EachRung_SitsOneStepHigher_InThatMonitorsPixels()
    {
        var origin = new ScreenPoint(1000, 600);
        var a = BookedPopLayout.TopLeftPx(origin, 0, 1.5, null);
        var b = BookedPopLayout.TopLeftPx(origin, 1, 1.5, null);
        Assert.Equal(a.X, b.X);
        Assert.InRange(a.Y - b.Y, BookedPopLayout.StackStepDip * 1.5 - 1, BookedPopLayout.StackStepDip * 1.5 + 1);
    }

    [Fact]
    public void ABubbleAtTheTopEdge_StillShowsItsNumberOnScreen()
    {
        var monitor = new Box(0, 0, 1920, 1080);
        var c = FigureCentre(BookedPopLayout.TopLeftPx(new ScreenPoint(5, 0), 3, 1.0, monitor), 1.0);
        Assert.InRange(c.X, monitor.X, monitor.Right);
        Assert.InRange(c.Y, monitor.Y + 20, monitor.Bottom);
    }

    [Fact]
    public void AZeroSizeMonitor_CountsAsUnknown_AndDoesNotClamp()
    {
        // Core Box.IsEmpty is W<=0||H<=0; WPF matched only Rect.Empty. Pin the Core rule.
        var origin = new ScreenPoint(5000, -300);
        Assert.Equal(BookedPopLayout.TopLeftPx(origin, 0, 1.0, null),
                     BookedPopLayout.TopLeftPx(origin, 0, 1.0, new Box(0, 0, 0, 0)));
    }

    [Fact]
    public void ASecondMonitorOrigin_StaysOnThatMonitor()
    {
        var monitor = new Box(1920, 0, 2560, 1440);
        var c = FigureCentre(BookedPopLayout.TopLeftPx(new ScreenPoint(4470, 1430), 0, 1.25, monitor), 1.25);
        Assert.InRange(c.X, monitor.X, monitor.Right);
        Assert.InRange(c.Y, monitor.Y, monitor.Bottom);
    }
}
