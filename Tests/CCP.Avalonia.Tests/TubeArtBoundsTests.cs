using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class TubeArtBoundsTests
{
    // WPF Windowing.cs:1090-1153: alpha >12, Uniform letterboxing, avatar union, cache per source.
    [Fact]
    public Task MeasuredInsetsFollowPixelsAvatarAndSourceChanges() => Run((main, tube) =>
    {
        var frame = tube.FindControl<Image>("ImgTubeFrame")!;
        var avatar = tube.FindControl<Border>("AvatarBorder")!;
        using var first = Art(10, 70);
        using var second = Art(2, 95);
        frame.Source = first;
        Dispatcher.UIThread.RunJobs();
        void Check(int min, int max)
        {
            double s = Math.Min(frame.Bounds.Width / 100, frame.Bounds.Height / 50);
            double off = (frame.Bounds.Width - 100 * s) / 2;
            var a = frame.TranslatePoint(new Point(off + min * s, 0), tube)!.Value;
            var b = frame.TranslatePoint(new Point(off + (max + 1) * s, 0), tube)!.Value;
            var al = avatar.TranslatePoint(default, tube)!.Value;
            var ar = avatar.TranslatePoint(new Point(avatar.Bounds.Width, 0), tube)!.Value;
            Assert.True(tube.TryMeasureArtInsetsPx(out var left, out var right, out var axis));
            Assert.Equal(Math.Max(0, (int)Math.Floor(Math.Min(a.X, al.X))), left);
            Assert.Equal(Math.Max(0, (int)Math.Floor(tube.Width - Math.Max(b.X, ar.X))), right);
            Assert.Equal((al.X + ar.X) / 2, axis, 6);
            Assert.Equal(main.Position.X - (int)Math.Round(tube.Width) + Math.Max(0, right - 3), tube.Position.X);
        }
        Check(10, 70);
        frame.Source = second;
        Dispatcher.UIThread.RunJobs();
        Check(2, 95);
        frame.IsVisible = false;
        Assert.False(tube.TryMeasureArtInsetsPx(out _, out _, out _));
        frame.IsVisible = true;
        using var empty = Art(-1, -1);
        frame.Source = empty;
        Assert.False(tube.TryMeasureArtInsetsPx(out _, out _, out _));
    });

    // WPF Windowing.cs:927,1044-1074,2263: right dock flips frame only, about her centre; detach resets.
    [Theory]
    [InlineData(0)]
    [InlineData(10)] // Sets 2+ translate her right; WPF Windowing.cs:1068 includes that transform.
    public Task RightDockMirrorsFrameAboutAvatarAndUsesReflectedInset(double avatarTranslation) => Run((main, tube) =>
    {
        var frame = tube.FindControl<Image>("ImgTubeFrame")!;
        var avatar = tube.FindControl<Border>("AvatarBorder")!;
        var sprite = tube.FindControl<Image>("ImgAvatar")!;
        var spriteTransform = sprite.RenderTransform;
        avatar.RenderTransform = new TranslateTransform(avatarTranslation, 0);
        using var art = Art(10, 70);
        frame.Source = art;
        Dispatcher.UIThread.RunJobs();
        Assert.True(tube.TryMeasureArtInsetsPx(out var left, out var right, out var axis));
        main.Position = new PixelPoint(0, 200);
        Dispatcher.UIThread.RunJobs();
        tube.UpdatePosition();
        Assert.Equal(-1, Assert.IsType<ScaleTransform>(frame.RenderTransform).ScaleX);
        Assert.Equal(avatar.Bounds.Center.X + avatarTranslation - frame.Bounds.X, frame.RenderTransformOrigin.Point.X, 6);
        int mirrored = Math.Max(0, (int)Math.Round(2 * axis - ((int)Math.Round(tube.Width) - right)) - 3);
        Assert.Equal(main.Position.X + (int)Math.Round(main.ClientSize.Width) - mirrored, tube.Position.X);
        Assert.Equal(mirrored, tube.InputRect!.Value.X);
        Assert.Same(spriteTransform, sprite.RenderTransform);
        // Measuring while flipped must still return the UNflipped bounds.
        Assert.True(tube.TryMeasureArtInsetsPx(out var flippedLeft, out var flippedRight, out var flippedAxis));
        Assert.Equal(left, flippedLeft);
        Assert.Equal(right, flippedRight);
        Assert.Equal(axis, flippedAxis, 6);
        tube.Detach();
        Assert.Null(frame.RenderTransform);
        tube.Attach();
        Assert.IsType<ScaleTransform>(frame.RenderTransform);
        main.Position = new PixelPoint(2000, 200);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(frame.RenderTransform);
    });

    private static Bitmap Art(int min, int max)
    {
        var bytes = new byte[100 * 50 * 4];
        // Barely visible pixels across the row must not inflate the painted bounds.
        for (int x = 0; x < 100; x++) bytes[x * 4 + 3] = 12;
        for (int x = min; x >= 0 && x <= max; x++) bytes[(100 + x) * 4 + 3] = 13;
        var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Unpremul, pin.AddrOfPinnedObject(), new PixelSize(100, 50), new Vector(96, 96), 400); }
        finally { pin.Free(); }
    }

    private static Task Run(Action<Window, AvatarTubeWindow> body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().WithInterFont()
                .With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        service.Current.AvatarTubeDetached = false;
        var main = new Window { Width = 1000, Height = 700, Position = new PixelPoint(2000, 200) };
        AvatarTubeWindow? tube = null;
        try
        {
            main.Show();
            tube = new AvatarTubeWindow(main);
            tube.Show();
            Dispatcher.UIThread.RunJobs();
            body(main, tube);
        }
        finally { tube?.Close(); main.Close(); CoreSettings.ServiceProvider = null; }
        return Task.CompletedTask;
    });
}
