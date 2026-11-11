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
using ConditioningControlPanel.Services.Billboard;
using ConditioningControlPanel.Services.Billboard.Board;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// The Tonight Board's tiles, ported from WPF BoardTileView (ConditioningControlPanel.Tests
/// BoardServiceTests): the arrival builds on the stepped beat and the clock stops once nothing moves,
/// effects keep it running at Full motion only, Motion Off shows a still board, a touch ripples even
/// while held, and a touch maps through the letterbox. With CCP_BILLBOARD_PARITY_SHOTS set it writes
/// port-board-*.png beside the WPF suite's wpf-board-*.png.
/// </summary>
public sealed class BillboardBoardTileTests
{
    /// <summary>A 64 x 36 test picture: a pink block framed in cyan on a dark field (the same pixels
    /// as ConditioningControlPanel.Tests BillboardArtParityShotsTests.BoardPixels).</summary>
    internal static BoardPicture Picture(params string[] fx)
    {
        const int w = 64, h = 36;
        var px = new int[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool frame = x >= 12 && x < 52 && y >= 8 && y < 28;
                bool inner = x >= 14 && x < 50 && y >= 10 && y < 26;
                px[y * w + x] = unchecked(inner ? (int)0xFFFF4FA8 : frame ? (int)0xFF5FE3FF : (int)0xFF120A20);
            }
        return BoardPicture.FromPixels(px, w, h, new BoardPost(4, fx, null, "lobby", BoardAudience.Everyone, 1, 6, w, h));
    }

    [Theory]
    [InlineData(0.5, 0.5, 640, 360, 32, 18)]
    [InlineData(0.0, 0.0, 640, 360, 0, 0)]
    [InlineData(0.999, 0.999, 640, 360, 63, 35)]
    [InlineData(0.5, 0.05, 640, 480, -1, -1)] // in the letterbox above the grid
    public void ATouchMapsThroughTheLetterbox(double nx, double ny, double w, double h, int tx, int ty)
    {
        bool hit = BoardTouch.TileAt(new Point(nx, ny), w, h, out var x, out var y);
        if (tx < 0) { Assert.False(hit); return; }
        Assert.True(hit);
        Assert.Equal((tx, ty), ((int)x, (int)y));
    }

    [Fact]
    public Task TheArrivalBuildsOnTheBeatThenTheClockStops() => WithBoard(MotionLevel.Full, Picture(), (view, time, pixels, shown) =>
    {
        Assert.Equal(4, shown()); // WPF OnLoaded reports the version once on screen
        var flat = pixels();
        Assert.False(view.Ticking); // never played: a flat, still board

        view.Play();
        Assert.True(view.Ticking);
        Step(time, 0.3);
        var building = pixels();
        Assert.False(building.AsSpan().SequenceEqual(flat), "the arrival lifts tiles");

        Step(time, BoardFxMath.BuildDoneSeconds + 0.5);
        Assert.False(view.Ticking); // no effects, one frame: nothing moves, nothing draws
        var landed = pixels();
        Assert.False(landed.AsSpan().SequenceEqual(building));
        Step(time, 1);
        Assert.True(pixels().AsSpan().SequenceEqual(landed));
    });

    [Fact]
    public Task EffectsKeepTheClockAtFullMotion() => WithBoard(MotionLevel.Full, Picture(BoardFx.Ola, BoardFx.Shine), (view, time, pixels, _) =>
    {
        view.Play();
        Step(time, BoardFxMath.BuildDoneSeconds + 0.5);
        Assert.True(view.Ticking);
        var a = pixels();
        Step(time, 0.7);
        Assert.False(pixels().AsSpan().SequenceEqual(a), "the effects roll");

        // Held: the clock stops and the frame stays.
        view.Pause();
        Assert.False(view.Ticking);
        var held = pixels();
        Step(time, 1);
        Assert.True(pixels().AsSpan().SequenceEqual(held));
    });

    [Fact]
    public Task ReducedMotionBuildsButLoopsNoEffects() => WithBoard(MotionLevel.Reduced, Picture(BoardFx.Ola, BoardFx.Shine), (view, time, _, _) =>
    {
        view.Play();
        Assert.True(view.Ticking); // the arrival is a transition
        Step(time, BoardFxMath.BuildDoneSeconds + 0.5);
        Assert.False(view.Ticking); // no ambient loops
    });

    [Fact]
    public Task MotionOffShowsAStillBoardAndIgnoresTouch() => WithBoard(MotionLevel.Off, Picture(BoardFx.Ola), (view, time, pixels, _) =>
    {
        var still = pixels();
        view.Play();
        Assert.False(view.Ticking);
        view.Touch(new Point(0.5, 0.5));
        Assert.False(view.Ticking);
        Step(time, 1);
        Assert.True(pixels().AsSpan().SequenceEqual(still));
    });

    [Fact]
    public Task ATouchRipplesWhileHeldAndSettles() => WithBoard(MotionLevel.Full, Picture(), (view, time, pixels, _) =>
    {
        view.Play();
        view.Pause(); // every tile lands at once
        Assert.False(view.Ticking);
        var landed = pixels();

        view.Touch(new Point(0.5, 0.5));
        Assert.True(view.Ticking);
        Step(time, 0.3);
        Assert.False(pixels().AsSpan().SequenceEqual(landed), "the ripple lifts tiles");
        Step(time, BoardFxMath.RippleDropAfter + 0.5);
        Assert.False(view.Ticking);

        view.Release();
        view.Play(); // after release: ignored, no throw
        view.Touch(new Point(0.5, 0.5));
        Assert.False(view.Ticking);
    });

    /// <summary>The WPF suite's board frames: never played, and played then held (landed).</summary>
    [Fact]
    public Task ShotsWhenAsked() => WithBoard(MotionLevel.Full, Picture(), (view, _, _, _) =>
    {
        var dir = Environment.GetEnvironmentVariable("CCP_BILLBOARD_PARITY_SHOTS");
        if (string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir);
        var host = (Control)view.Parent!;
        Save(host, Path.Combine(dir, "port-board-still.png"));
        view.Play();
        view.Pause();
        Save(host, Path.Combine(dir, "port-board-landed.png"));
    });

    private static void Step(SteppedTime time, double seconds)
    {
        for (double s = 0; s < seconds; s += 1.0 / 30)
        {
            time.Advance(TimeSpan.FromMilliseconds(1000.0 / 30));
            BeatLoop.StepAll();
        }
    }

    private static void Save(Control c, string path)
    {
        using var bmp = new RenderTargetBitmap(new PixelSize(1000, 420));
        bmp.Render(c);
        bmp.Save(path);
    }

    private static byte[] Pixels(Control c)
    {
        using var bmp = new RenderTargetBitmap(new PixelSize(1000, 420));
        bmp.Render(c);
        var px = new byte[1000 * 420 * 4];
        var buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(px.Length);
        try
        {
            bmp.CopyPixels(new PixelRect(0, 0, 1000, 420), buffer, px.Length, 1000 * 4);
            System.Runtime.InteropServices.Marshal.Copy(buffer, px, 0, px.Length);
        }
        finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer); }
        return px;
    }

    private static Task WithBoard(MotionLevel level, BoardPicture picture, Action<BoardTileView, SteppedTime, Func<byte[]>, Func<int>> check) => AvaloniaTestDispatcher.RunAsync(() =>
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
        int shown = 0;
        var view = new BoardTileView(new BoardArtData(picture, v => shown = v));
        var host = new Panel { Width = 1000, Height = 420, Background = new SolidColorBrush(Color.FromRgb(0x12, 0x10, 0x1F)), Children = { view } };
        var window = new Window { Width = 1000, Height = 420, Content = host };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try { check(view, time, () => Pixels(host), () => shown); }
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
