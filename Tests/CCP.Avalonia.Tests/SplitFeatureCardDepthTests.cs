using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Models;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class SplitFeatureCardDepthTests
{
    [Fact]
    public Task DashboardClicksInvertAndRefreshFxRegatesWithoutReshow() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions
                { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var saved = (s.MotionLevel, s.PerformanceMode, s.DashboardInvertClicks);
        s.MotionLevel = MotionLevel.Full;
        s.PerformanceMode = false;
        var card = new SplitFeatureCard { Width = 220, Height = 220, DashboardDepth = true, IsActiveA = true, HelpSectionIdA = "MindWipe" };
        var window = new Window { Width = 250, Height = 250, Content = card };
        int clicksA = 0, togglesA = 0, clicksB = 0, togglesB = 0;
        card.ClickA += (_, _) => clicksA++;
        card.ToggleA += (_, _) => togglesA++;
        card.ClickB += (_, _) => clicksB++;
        card.ToggleB += (_, _) => togglesB++;
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var a = card.TranslatePoint(new Point(20, 20), window)!.Value;
            var b = card.TranslatePoint(new Point(200, 200), window)!.Value;
            void Click(Point p, MouseButton button)
            {
                window.MouseDown(p, button);
                window.MouseUp(p, button);
                Dispatcher.UIThread.RunJobs();
            }
            s.DashboardInvertClicks = false;
            Click(a, MouseButton.Left); Click(b, MouseButton.Right);
            Assert.Equal((1, 0, 0, 1), (clicksA, togglesA, clicksB, togglesB));
            s.DashboardInvertClicks = true;
            Click(a, MouseButton.Left); Click(b, MouseButton.Right);
            Assert.Equal((1, 1, 1, 1), (clicksA, togglesA, clicksB, togglesB));
            card.DashboardDepth = false;
            Click(a, MouseButton.Left);
            Assert.Equal(2, clicksA); // inversion is dashboard-only
            var help = card.FindControl<Button>("BtnHelpA")!;
            Assert.True(help.IsVisible);
            Click(help.TranslatePoint(new Point(help.Bounds.Width / 2, help.Bounds.Height / 2), window)!.Value, MouseButton.Left);
            Assert.Equal((2, 1, 1, 1), (clicksA, togglesA, clicksB, togglesB));
            global::ConditioningControlPanel.Avalonia.Controls.HelpPopover.CloseActive();
            Dispatcher.UIThread.RunJobs();
            var glow = card.FindControl<Border>("GlowLayer")!;
            // The shell re-gates via ApplyDashboardFxLoops -> RefreshFx on Env.MotionGateChanged.
            s.PerformanceMode = true;
            card.RefreshFx();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, glow.Opacity);
            s.PerformanceMode = false;
            s.MotionLevel = MotionLevel.Reduced;
            card.RefreshFx();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0.90, glow.Opacity, 3);
            s.MotionLevel = MotionLevel.Off;
            card.RefreshFx();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, glow.Opacity);
        }
        finally { window.Close(); (s.MotionLevel, s.PerformanceMode, s.DashboardInvertClicks) = saved; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task GreyArtRegistersWithColourAndFadesByHalfState() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions
                { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var motion = s.MotionLevel;
        s.MotionLevel = MotionLevel.Off;
        using var art = new global::Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(640, 320), new Vector(96, 96),
            global::Avalonia.Platform.PixelFormat.Bgra8888, global::Avalonia.Platform.AlphaFormat.Premul);
        using (var data = art.Lock())
        {
            var pixels = new byte[data.RowBytes * 320];
            for (int i = 0; i < pixels.Length; i += 4) { pixels[i + 2] = 255; pixels[i + 3] = 255; }
            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, data.Address, pixels.Length);
        }
        var card = new SplitFeatureCard { Width = 220, Height = 220, IconA = art, IconB = art };
        var window = new Window { Width = 260, Height = 260, Content = card };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var a = card.FindControl<Border>("HalfMuteA")!;
            var b = card.FindControl<Border>("HalfMuteB")!;
            Assert.NotNull(a); Assert.NotNull(b);
            Assert.Equal(1, a.Opacity); Assert.Equal(1, b.Opacity);
            var brushA = Assert.IsType<ImageBrush>(a.Background);
            var brushB = Assert.IsType<ImageBrush>(b.Background);
            Assert.Same(brushA.Source, brushB.Source);
            Assert.Equal(new PixelSize(320, 160), ((global::Avalonia.Media.Imaging.Bitmap)brushA.Source!).PixelSize);
            using (var encoded = new MemoryStream())
            {
                ((global::Avalonia.Media.Imaging.Bitmap)brushA.Source!).Save(encoded);
                using var decoded = global::SkiaSharp.SKBitmap.Decode(encoded.ToArray());
                var pixel = decoded.GetPixel(0, 0);
                Assert.Equal((byte)76, pixel.Red);
                Assert.Equal(pixel.Red, pixel.Green); Assert.Equal(pixel.Red, pixel.Blue);
                Assert.Equal((byte)255, pixel.Alpha);
            }
            Assert.Equal(Stretch.UniformToFill, brushA.Stretch);
            window.MouseMove(card.TranslatePoint(new Point(20, 20), window)!.Value);
            Assert.Equal(0, a.Opacity); Assert.Equal(1, b.Opacity);
            card.DashboardDepth = true;
            Assert.Equal(1, a.Opacity); // dashboard hover keeps off art grey
            card.IsActiveA = true;
            Assert.Equal(0, a.Opacity);
            card.IconA = null;
            Assert.Null(a.Background);
            s.MotionLevel = MotionLevel.Full;
            card.DashboardDepth = false;
            card.IsActiveA = false;
            int snaps = 0;
            a.PropertyChanged += (_, e) => { if (e.Property == global::Avalonia.Animation.Animatable.TransitionsProperty && e.NewValue == null) snaps++; };
            card.IsVisible = false;
            Assert.True(snaps > 0, "WPF ResetSplit snaps mute on hide even when transitions are allowed");
            Assert.Equal(1, a.Opacity);
        }
        finally { window.Close(); s.MotionLevel = motion; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task EachHalfPressesIntoItsOwnSocketAndReleasesOnExit() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions
                { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var motion = s.MotionLevel;
        s.MotionLevel = MotionLevel.Off;
        var card = new SplitFeatureCard { Width = 220, Height = 220, DashboardDepth = true, IsActiveA = true };
        var window = new Window { Width = 260, Height = 260, Content = card };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var faceA = card.FindControl<Grid>("HalfFaceA");
            var faceB = card.FindControl<Grid>("HalfFaceB");
            Assert.NotNull(faceA); Assert.NotNull(faceB);
            double Travel(Grid face) => ((TranslateTransform)((TransformGroup)face.RenderTransform!).Children[1]).Y;
            Assert.Equal(1, Travel(faceA)); Assert.Equal(-1, Travel(faceB));
            Assert.True(card.FindControl<Border>("HalfSocketA")!.IsVisible);
            var a = card.TranslatePoint(new Point(20, 20), window)!.Value;
            window.MouseDown(a, MouseButton.Left);
            Assert.Equal(2, Travel(faceA)); Assert.Equal(-1, Travel(faceB));
            window.MouseMove(new Point(255, 255));
            Assert.Equal(1, Travel(faceA));
            window.MouseUp(new Point(255, 255), MouseButton.Left);
            card.IsVisible = false;
            Assert.Equal(1, Travel(faceA));
        }
        finally { window.Close(); s.MotionLevel = motion; }
        return Task.CompletedTask;
    });
}
