using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls.Billboard;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// The living poster, ported from WPF (ConditioningControlPanel.Tests BillboardLivingPosterTests):
/// every pose keeps the picture covering the card, the settled crop stays near 4 percent, the landing
/// pop and the sheen come and go, the view moves on the shared beat (stepped here), holds still below
/// Full motion, and Pause / Release stop the clock. With CCP_BILLBOARD_PARITY_SHOTS set it writes
/// port-poster-*.png beside the WPF suite's wpf-poster-*.png.
/// </summary>
public sealed class BillboardLivingPosterTests
{
    private const string Poster = "features/deeper.png";

    [Theory]
    [InlineData(1600, 900)]
    [InlineData(1000, 420)]
    [InlineData(800, 600)]
    public void EveryPoseCoversTheCard(double w, double h)
    {
        var rng = new Random(7);
        for (int i = 0; i < 4000; i++)
        {
            var pointer = new Vector(rng.NextDouble() * 2 - 1, rng.NextDouble() * 2 - 1);
            double energy = rng.NextDouble(), phase = rng.NextDouble() * Math.Tau * 3;
            double land = PosterMotion.Land(rng.NextDouble() * PosterMotion.LandSeconds);
            var pose = PosterMotion.At(phase, pointer, energy, land, w, h);
            Assert.True(PosterMotion.Covers(pose, w, h), $"edge shows at {pose}");
        }
    }

    [Fact]
    public void TheSettledCropStaysNearFourPercentOnAPosterShapedCard()
    {
        var rng = new Random(11);
        double max = 0;
        for (int i = 0; i < 4000; i++)
        {
            var pose = PosterMotion.At(rng.NextDouble() * Math.Tau, new Vector(rng.NextDouble() * 2 - 1, rng.NextDouble() * 2 - 1), rng.NextDouble(), 0, 1600, 900);
            max = Math.Max(max, pose.Scale);
        }
        Assert.InRange(max, PosterMotion.BaseScale, 1.0405);
        Assert.Equal(PosterMotion.BaseScale, PosterMotion.Still.Scale);
    }

    [Fact]
    public void TheLandingPopKicksDipsAndSettles()
    {
        Assert.Equal(PosterMotion.LandKick, PosterMotion.Land(0), 6);
        Assert.Equal(0, PosterMotion.Land(PosterMotion.LandSeconds));
        Assert.Equal(0, PosterMotion.Land(-1));
        Assert.True(PosterMotion.Land(PosterMotion.LandSeconds * 0.66) < 0);
        Assert.True(PosterMotion.Land(PosterMotion.LandSeconds * 0.66) > -PosterMotion.LandKick * 0.2);
    }

    [Fact]
    public void TheSheenSweepsNowAndThen()
    {
        Assert.Equal(0, PosterMotion.Sheen(0));
        Assert.InRange(PosterMotion.Sheen(PosterMotion.SweepSeconds / 2), 0.49, 0.51);
        Assert.Equal(-1, PosterMotion.Sheen(PosterMotion.SweepSeconds + 0.5));
        Assert.Equal(0, PosterMotion.Sheen(PosterMotion.SheenCycleSeconds), 6);
        Assert.Equal(-1, PosterMotion.Sheen(double.NaN));
    }

    [Fact]
    public void APosterWithoutADrawnSceneLives()
    {
        // WPF HouseScenes.Create: any other poster keeps the living PosterArtView.
        Assert.IsType<PosterArtView>(HouseScenes.Create(Poster));
        Assert.IsType<PosterArtView>(HouseScenes.Create(null));
    }

    [Fact]
    public Task ThePosterMovesOnTheBeatAndStopsItsClock() => WithPoster(MotionLevel.Full, (view, time) =>
    {
        var still = view.CurrentMatrix;
        Assert.Equal(PosterMotion.BaseScale, still.M11, 3);

        // Played: the landing pop, then the breath and pan on the shared beat.
        view.Play();
        Assert.True(view.IsAnimating);
        for (int i = 0; i < 6; i++) { time.Advance(TimeSpan.FromMilliseconds(1000.0 / 30)); BeatLoop.StepAll(); }
        var landing = view.CurrentMatrix;
        Assert.True(landing.M11 > PosterMotion.BaseScale, "the landing pop zooms in");
        for (int i = 0; i < 60; i++) { time.Advance(TimeSpan.FromMilliseconds(1000.0 / 30)); BeatLoop.StepAll(); }
        var moving = view.CurrentMatrix;
        Assert.NotEqual(still, moving);
        Assert.NotEqual(landing, moving);

        // Held: the matrix stays where it was.
        view.Pause();
        Assert.False(view.IsAnimating);
        time.Advance(TimeSpan.FromSeconds(3));
        BeatLoop.StepAll();
        Assert.Equal(moving, view.CurrentMatrix);

        // Hover leans the picture toward the pointer (WPF Step).
        view.Step(0.4, hovered: true, new Point(0.9, 0.2));
        view.Step(0.4, hovered: true, new Point(0.9, 0.2));
        Assert.NotEqual(moving, view.CurrentMatrix);

        // Released for good.
        view.Play();
        view.Release();
        Assert.False(view.IsAnimating);
        view.Play();
        Assert.False(view.IsAnimating);
    });

    [Theory]
    [InlineData(MotionLevel.Reduced)]
    [InlineData(MotionLevel.Off)]
    public Task BelowFullMotionThePosterHoldsTheStillPose(MotionLevel level) => WithPoster(level, (view, time) =>
    {
        // WPF Play: ambient loops off = the still pose, no clock.
        view.Play();
        Assert.False(view.IsAnimating);
        time.Advance(TimeSpan.FromSeconds(2));
        BeatLoop.StepAll();
        Assert.Equal(PosterMotion.BaseScale, view.CurrentMatrix.M11, 3);
        Assert.Equal(0, view.CurrentMatrix.M21, 6);
    });

    /// <summary>The WPF suite's poster frames (BillboardArtParityShotsTests): still, landing, rest
    /// and hover after the same fixed steps.</summary>
    [Fact]
    public Task ShotsWhenAsked() => WithPoster(MotionLevel.Full, (view, _) =>
    {
        var dir = Environment.GetEnvironmentVariable("CCP_BILLBOARD_PARITY_SHOTS");
        if (string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir);
        var host = (Control)view.Parent!;
        Save(host, Path.Combine(dir, "port-poster-still.png"));
        view.Play();
        view.Step(0.05, false, default);
        Save(host, Path.Combine(dir, "port-poster-land.png"));
        for (int i = 0; i < 20; i++) view.Step(0.033, false, default);
        Save(host, Path.Combine(dir, "port-poster-rest.png"));
        for (int i = 0; i < 30; i++) view.Step(0.033, true, new Point(0.85, 0.25));
        Save(host, Path.Combine(dir, "port-poster-hover.png"));
    });

    private static void Save(Control c, string path)
    {
        using var bmp = new RenderTargetBitmap(new PixelSize(1000, 420));
        bmp.Render(c);
        bmp.Save(path);
    }

    private static Task WithPoster(MotionLevel level, Action<PosterArtView, SteppedTime> check) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var old = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        CoreSettings.Current.MotionLevel = level;
        CoreSettings.Current.PerformanceMode = CoreSettings.Current.AutoPerformanceMode = false;
        var oldTime = BeatLoop.Time;
        var time = new SteppedTime();
        BeatLoop.Time = time;
        var view = new PosterArtView(Poster) { Accent = Color.Parse("#9b7bff") };
        var host = new Panel { Width = 1000, Height = 420, Background = new SolidColorBrush(Color.FromRgb(0x12, 0x10, 0x1F)), Children = { view } };
        var window = new Window { Width = 1000, Height = 420, Content = host };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try { check(view, time); }
        finally
        {
            window.Close();
            view.Release();
            BeatLoop.Time = oldTime;
            CoreSettings.ServiceProvider = old;
        }
        return Task.CompletedTask;
    });
}
