using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

public class EmiGlassSaverMotionTests
{
    [Theory]
    [InlineData(MotionLevel.Full, true)]
    [InlineData(MotionLevel.Reduced, false)]
    [InlineData(MotionLevel.Off, false)]
    public void PongRequiresFullMotion(MotionLevel level, bool expected) =>
        Assert.Equal(expected, EmiDeskWindow.PongFeasible(level));

    [Fact]
    public Task PongMovesOnTick() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        AmbientBeatLoopsTests.Setup();
        var pong = new EmiDeskWindow.PongPainter(63, 58, new Random(1));
        pong.Attach(new Canvas());
        var x = pong.BallX;
        pong.Tick(33);
        Assert.NotEqual(x, pong.BallX);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(63.3536, 57.86346263096624)]
    [InlineData(175.056, 159.8858835855646)]
    public Task PongRendersAtGlassSizes(double w, double h) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        AmbientBeatLoopsTests.Setup();
        var canvas = new Canvas { Width = w, Height = h, ClipToBounds = true };
        var pong = new EmiDeskWindow.PongPainter(w, h, new Random(1));
        pong.Attach(canvas);
        pong.Tick(33);
        canvas.Measure(new Size(w, h)); canvas.Arrange(new Rect(0, 0, w, h));
        using var frame = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(w), (int)Math.Ceiling(h)));
        frame.Render(canvas);
        Assert.True(canvas.Children.Count > 5);
        return Task.CompletedTask;
    });

    [Fact]
    public Task ASeededFumbleLetsAPointThrough() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        AmbientBeatLoopsTests.Setup();
        var pong = new EmiDeskWindow.PongPainter(60, 54, new Random(5));
        pong.Attach(new Canvas());
        for (int ms = 33; ms <= 10000 && pong.Points == 0; ms += 33) pong.Tick(ms);
        Assert.True(pong.Points > 0);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(MotionLevel.Full, true)]
    [InlineData(MotionLevel.Reduced, false)]
    [InlineData(MotionLevel.Off, false)]
    public Task IdlePathRespectsMotion(MotionLevel level, bool pong) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        AmbientBeatLoopsTests.Setup();
        var old = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var bedtime = EmiState.Current.BedtimeUntil;
        var desk = new EmiDeskWindow { GlassChannelOverride = "pong" };
        try
        {
            EmiState.Current.BedtimeUntil = DateTime.MinValue;
            CoreSettings.Current.EmiDeskGlass = true;
            CoreSettings.Current.MotionLevel = level;
            desk.Show(); Dispatcher.UIThread.RunJobs();
            var start = DateTime.UtcNow.AddSeconds(61);
            desk.PollGlass(start); desk.AdvanceGlass(start.AddMilliseconds(220));
            var canvas = Assert.IsType<Canvas>(Assert.Single(desk.GlassHost.Children));
            Assert.Equal(pong, canvas.Children.OfType<TextBlock>().Any(t => t.Text == "0"));
            if (pong)
            {
                CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
                desk.AdvanceGlass(start.AddMilliseconds(253));
                Assert.False(desk.GlassIsLive);
            }
        }
        finally { desk.ShutDown(); EmiState.Current.BedtimeUntil = bedtime; CoreSettings.ServiceProvider = old; }
        return Task.CompletedTask;
    });

    // One director (b209/b210): PollGlass hands GlassPick ONE pool of the feasible channels (WPF EmiChannels.Pick).
    [Theory]
    [InlineData(MotionLevel.Full, true)]
    [InlineData(MotionLevel.Reduced, false)]
    public Task DirectorOffersOnePoolOfFeasibleChannels(MotionLevel level, bool pong) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        AmbientBeatLoopsTests.Setup();
        var old = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var bedtime = EmiState.Current.BedtimeUntil;
        var oldPick = EmiDeskWindow.GlassPick;
        string[]? seen = null;
        EmiDeskWindow.GlassPick = pool => { seen = pool.ToArray(); return "offair"; };
        var desk = new EmiDeskWindow();
        try
        {
            EmiState.Current.BedtimeUntil = DateTime.MinValue;
            CoreSettings.Current.EmiDeskGlass = true;
            CoreSettings.Current.MotionLevel = level;
            desk.Show(); Dispatcher.UIThread.RunJobs();
            desk.PollGlass(DateTime.UtcNow.AddSeconds(61));
            Assert.NotNull(seen);
            Assert.Equal(pong, seen!.Contains("pong"));
            Assert.Contains("shop", seen);
            Assert.Contains("offair", seen);
            Assert.Equal(seen.Length, seen.Distinct().Count());
            Assert.True(desk.GlassIsLive);
        }
        finally
        {
            desk.ShutDown(); EmiDeskWindow.GlassPick = oldPick;
            EmiState.Current.BedtimeUntil = bedtime; CoreSettings.ServiceProvider = old;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task DirectorLandsRerunsOnlyWithATape() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        AmbientBeatLoopsTests.Setup();
        var old = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var bedtime = EmiState.Current.BedtimeUntil;
        var path = System.IO.Path.Combine(CorePaths.UserData, "arcademy_meta.json");
        var bytes = System.IO.File.Exists(path) ? System.IO.File.ReadAllBytes(path) : null;
        var desk = new EmiDeskWindow { GlassChannelOverride = "reruns" };
        try
        {
            EmiState.Current.BedtimeUntil = DateTime.MinValue;
            CoreSettings.Current.EmiDeskGlass = true;
            desk.Show(); Dispatcher.UIThread.RunJobs();
            System.IO.File.Delete(path); RerunsPainter.ResetCacheForTests();
            var flip = DateTime.UtcNow.AddSeconds(61);
            desk.PollGlass(flip); desk.AdvanceGlass(flip.AddMilliseconds(220));
            var layer = Assert.IsType<Canvas>(Assert.Single(desk.GlassHost.Children));
            Assert.DoesNotContain(layer.Children.OfType<TextBlock>(), t => t.Text == "<< REW");
            desk.NoteGlassActivity();

            System.IO.File.WriteAllText(path, "{\"days\":{\"2026-01-01\":{\"classes\":{\"test_game\":{\"grade\":\"s\"}}}}}");
            RerunsPainter.ResetCacheForTests();
            flip = DateTime.UtcNow.AddSeconds(61);
            desk.PollGlass(flip); desk.AdvanceGlass(flip.AddMilliseconds(220));
            layer = Assert.IsType<Canvas>(Assert.Single(desk.GlassHost.Children));
            Assert.Contains(layer.Children.OfType<TextBlock>(), t => t.Text == "<< REW");
            Assert.Contains(layer.Children.OfType<TextBlock>(), t => t.Text == "TEST GAME");
        }
        finally
        {
            desk.ShutDown();
            try { if (bytes == null) System.IO.File.Delete(path); else System.IO.File.WriteAllBytes(path, bytes); }
            finally { RerunsPainter.ResetCacheForTests(); }
            EmiState.Current.BedtimeUntil = bedtime; CoreSettings.ServiceProvider = old;
        }
        return Task.CompletedTask;
    });
}
