using System;
using ConditioningControlPanel.Controls.Preview;
using ConditioningControlPanel.Models;
using SkiaSharp;
using Xunit;

namespace CCP.Avalonia.Tests;

public class PreviewFoundationTests
{
    private sealed class Scene : IPreviewScene
    {
        public string[] Settings => new[] { "Watched" };
        public int Starts, Changes, Clicks, Ticks;
        public double LastStep;
        public void Layout(SKRect screen, SKRect visible) { }
        public void Start() => Starts++;
        public void Tick(double dt) { Ticks++; LastStep = dt; }
        public void Render(SKCanvas canvas, SKRect screen, SKRect visible) { }
        public void SettingChanged(string name) => Changes++;
        public void Click(SKPoint at) => Clicks++;
        public void Dispose() { }
    }

    [Fact]
    public void GateHonoursMotionVisibilityAndPanelQuiet()
    {
        Assert.Equal(PreviewPace.Hold, PreviewGate.Pace(MotionLevel.Full, true, true, true));
        Assert.Equal(PreviewPace.Live, PreviewGate.Pace(MotionLevel.Reduced, false, false, true));
        Assert.Equal(PreviewPace.Still, PreviewGate.Pace(MotionLevel.Off, false, true, true));
        Assert.Equal(PreviewPace.Still, PreviewGate.Pace(MotionLevel.Full, false, false, true));
        Assert.True(PreviewGate.CanShow(true, true, true, false));
        Assert.False(PreviewGate.CanShow(true, true, true, true));
    }

    [Fact]
    public void DriverWakesForWatchedInputAndExpiresWithClampedFrames()
    {
        double now = 0;
        var scene = new Scene();
        var driver = new PreviewDriver(scene, () => now);
        Assert.False(driver.SettingChanged("Other"));
        Assert.True(driver.Open());
        Assert.Equal(1, scene.Starts);
        Assert.True(driver.Frame());
        now = 2;
        Assert.True(driver.Frame());
        Assert.Equal(.1, scene.LastStep, 6);
        Assert.True(driver.SettingChanged("Watched"));
        Assert.Equal(1, scene.Changes);
        now = 12;
        Assert.False(driver.WantsClock);
        Assert.False(driver.Frame());
        driver.Pace = PreviewPace.Hold;
        Assert.False(driver.Click(new SKPoint(1, 2)));
        Assert.Equal(0, scene.Clicks);
        driver.Pace = PreviewPace.Still;
        Assert.True(driver.Click(new SKPoint(1, 2)));
        Assert.Equal(1, scene.Clicks);
        Assert.True(scene.Ticks > 2);
        Assert.False(driver.WantsClock);
    }

    [Fact]
    public void PicturesHaveIndependentImagesAndExpectedAspect()
    {
        using var a = PreviewPictures.Make(-1, .74f, 45, 100);
        using var b = PreviewPictures.Make(5, 1.36f, 45, 100);
        Assert.NotSame(a, b);
        Assert.Equal(74, a.Width);
        Assert.Equal(100, a.Height);
        Assert.Equal(100, b.Width);
        Assert.Equal(73, b.Height);
    }

    [Fact]
    public void DesktopFitsWindowAboveShortTaskbar()
    {
        using var bitmap = new SKBitmap(300, 220);
        using var canvas = new SKCanvas(bitmap);
        PreviewDesktop.Paint(canvas, 300, 220);
        Assert.Equal(PreviewDesktop.Css(0xf3f4f8), bitmap.GetPixel(150, 160));
        Assert.NotEqual(PreviewDesktop.Css(0xf3f4f8), bitmap.GetPixel(150, 185));
    }
}
