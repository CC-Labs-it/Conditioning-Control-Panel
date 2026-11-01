using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class SingleFeatureCardMuteTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public Task RestArtHoverAndGuardsFollowWpf() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var settings = CoreSettings.Current;
        var motion = settings.MotionLevel;
        settings.MotionLevel = MotionLevel.Off;
        using var art = new WriteableBitmap(new PixelSize(2, 1), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var pixels = art.Lock())
            Marshal.Copy(new byte[] { 0, 0, 255, 255, 0, 0, 255, 255 }, 0, pixels.Address, 8);
        var card = new FeatureCard { Width = 180, Height = 140, Icon = art, DimWhenInactive = true };
        var window = new Window { Width = 240, Height = 200, Content = card };
        window.Show();
        try
        {
            window.UpdateLayout();
            var content = card.FindControl<Grid>("ContentRoot")!;
            var mute = card.FindControl<Border>("ImgIconMute")!;
            var title = card.FindControl<TextBlock>("TxtTitle")!;
            Assert.NotNull(mute);
            Assert.Equal(0.62, content.Opacity);
            Assert.Equal(0.72, title.Opacity);
            Assert.Equal(1, mute.Opacity);
            var brush = Assert.IsType<ImageBrush>(mute.Background);
            Assert.Equal(Stretch.UniformToFill, brush.Stretch);
            using (var encoded = new MemoryStream())
            {
                ((Bitmap)brush.Source!).Save(encoded);
                using var decoded = SkiaSharp.SKBitmap.Decode(encoded.ToArray());
                var pixel = decoded.GetPixel(0, 0);
                Assert.Equal((byte)76, pixel.Red);
                Assert.Equal(pixel.Red, pixel.Green);
                Assert.Equal(pixel.Red, pixel.Blue);
            }
            window.MouseMove(card.TranslatePoint(new Point(40, 40), window)!.Value);
            Assert.Equal(1, content.Opacity);
            Assert.Equal(1, title.Opacity);
            Assert.Equal(0, mute.Opacity);
            card.DashboardDepth = true;
            Assert.Equal(0.62, content.Opacity); // Home stays muted while hovered
            Assert.Equal(1, mute.Opacity);
            card.IsActive = true;
            Assert.Equal(1, content.Opacity);
            Assert.Equal(0, mute.Opacity);
            card.IsActive = false;
            card.SetTutorialPreview(true);
            Assert.Equal(1, content.Opacity);
            Assert.Equal(0, mute.Opacity);
            card.SetTutorialPreview(false);
            card.IsLocked = true;
            Assert.Equal(0.35, content.Opacity);
            Assert.Equal(1, title.Opacity);
            Assert.Equal(0, mute.Opacity);
            card.IsLocked = false;
            card.TeaseTier = 1;
            Assert.Equal(0.62, content.Opacity);
            Assert.Equal(1, title.Opacity);
            Assert.Equal(0, mute.Opacity);
            card.TeaseTier = 0;
            Assert.Equal(1, mute.Opacity);
            card.DimWhenInactive = false; // navigation-only tile
            Assert.Equal(1, content.Opacity);
            Assert.Equal(1, title.Opacity);
            Assert.Equal(0, mute.Opacity);
            card.Icon = null;
            Assert.Null(mute.Background);
            card.Icon = art;
            Assert.Same(brush.Source, ((ImageBrush)mute.Background!).Source); // cached twin
            using var replacement = new WriteableBitmap(new PixelSize(1, 1), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            card.Icon = replacement;
            Assert.NotSame(brush.Source, ((ImageBrush)mute.Background!).Source);

            settings.MotionLevel = MotionLevel.Full;
            card.DimWhenInactive = true;
            var transition = Assert.IsType<DoubleTransition>(Assert.Single(mute.Transitions!));
            Assert.Equal(TimeSpan.FromMilliseconds(200), transition.Duration);
            settings.MotionLevel = MotionLevel.Off;
            card.IsActive = true;
            Assert.Null(mute.Transitions);
            Assert.Equal(0, mute.Opacity);
        }
        finally { window.Close(); settings.MotionLevel = motion; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task DashboardToggleRepaintsSingleCard() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var old = CoreSettings.ServiceProvider;
        var session = CoreSession.IsSessionRunningProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreSession.IsSessionRunningProvider = () => false;
        service.Current.MotionLevel = MotionLevel.Off;
        service.Current.FlashEnabled = false;
        service.Current.DashboardInvertClicks = false;
        var shell = new MainShellWindow { Width = 1400, Height = 1000 };
        shell.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); shell.UpdateLayout();
            var card = shell.SettingsPage!.CardFlash;
            var content = card.FindControl<Grid>("ContentRoot")!;
            var mute = card.FindControl<Border>("ImgIconMute")!;
            Assert.Equal(0.62, content.Opacity);
            Assert.Equal(1, mute.Opacity);
            var at = card.TranslatePoint(new Point(card.Bounds.Width / 2, card.Bounds.Height / 2), shell)!.Value;
            shell.MouseMove(at); shell.MouseDown(at, MouseButton.Right); shell.MouseUp(at, MouseButton.Right);
            Dispatcher.UIThread.RunJobs();
            Assert.True(service.Current.FlashEnabled);
            Assert.True(card.IsActive);
            Assert.Equal(1, content.Opacity);
            Assert.Equal(0, mute.Opacity);
            shell.MouseDown(at, MouseButton.Right); shell.MouseUp(at, MouseButton.Right);
            Dispatcher.UIThread.RunJobs();
            Assert.False(service.Current.FlashEnabled);
            Assert.Equal(0.62, content.Opacity);
            Assert.Equal(1, mute.Opacity);
        }
        finally { shell.Close(); CoreSession.IsSessionRunningProvider = session; CoreSettings.ServiceProvider = old; }
        return Task.CompletedTask;
    });
}
