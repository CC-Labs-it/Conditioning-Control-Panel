using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Helpers;
using Xunit;

namespace CCP.Avalonia.Tests;

public class ChaosHubLogoTests
{
    [Fact]
    public void Logo_art_and_missing_art_fallback() => AvaloniaTestDispatcher.Run(() =>
    {
        var enabled = CoreSettings.Current.ChaosSkiaFxEnabled;
        ChaosHubWindow? hub = null;
        try
        {
            CoreSettings.Current.ChaosSkiaFxEnabled = false;
            ChaosArt.RootsOverride = () => new[] { Path.Combine(AppContext.BaseDirectory, "assets", "Chaos") };
            hub = new ChaosHubWindow();
            hub.Show();
            Dispatcher.UIThread.RunJobs();
            var slot = hub.FindControl<Border>("MenuLogoFx")!;
            Assert.Equal(314, slot.Width);
            Assert.Equal(224, slot.Height);
            Assert.Equal(314, slot.Bounds.Width);
            Assert.Equal(224, slot.Bounds.Height);
            using var bitmap = new global::Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(314, 224));
            bitmap.Render(slot);
            var pixels = Pixels(bitmap);
            Assert.True(Enumerable.Range(0, pixels.Length / 4).Count(i => pixels[i * 4 + 3] > 0) > 1000);
            Assert.NotNull(hub.FindControl<Image>("MenuLogoImage")!.Source);
            Assert.False(hub.FindControl<StackPanel>("MenuLogoFallback")!.IsVisible);
            hub.Close();
            ChaosArt.RootsOverride = () => new[] { Path.Combine(AppContext.BaseDirectory, "missing-logo-root") };
            hub = new ChaosHubWindow();
            Assert.Null(hub.FindControl<Image>("MenuLogoImage")!.Source);
            Assert.True(hub.FindControl<StackPanel>("MenuLogoFallback")!.IsVisible);
            hub.Close();
        }
        finally { hub?.Close(); CoreSettings.Current.ChaosSkiaFxEnabled = enabled; ChaosArt.ResetForTest(); }
    });

    [Fact]
    public void Twinkle_spawning_obeys_enhanced_fx_gate() => AvaloniaTestDispatcher.Run(() =>
    {
        var enabled = CoreSettings.Current.ChaosSkiaFxEnabled;
        Window? host = null;
        try
        {
            ChaosArt.RootsOverride = () => new[] { Path.Combine(AppContext.BaseDirectory, "assets", "Chaos") };
            var fx = new ChaosHubLogoFx { Width = 314, Height = 224 };
            host = new Window { Width = 314, Height = 224, Content = fx };
            CoreSettings.Current.ChaosSkiaFxEnabled = false;
            host.Show();
            Assert.True(fx.SpotCount > 0);
            CoreSettings.Current.ChaosSkiaFxEnabled = false;
            for (int i = 0; i < 100; i++) fx.Step();
            Assert.Equal(0, fx.TwinkleCount);
            CoreSettings.Current.ChaosSkiaFxEnabled = true;
            fx.Step();
            Assert.Equal(1, fx.TwinkleCount);
        }
        finally { host?.Close(); CoreSettings.Current.ChaosSkiaFxEnabled = enabled; ChaosArt.ResetForTest(); }
    });

    [Fact]
    public void Animation_uses_shared_beat_and_parks_with_its_visual() => AvaloniaTestDispatcher.Run(() =>
    {
        var previousTime = BeatLoop.Time;
        var enabled = CoreSettings.Current.ChaosSkiaFxEnabled;
        var time = new SteppedTime();
        BeatLoop.Time = time;
        ChaosArt.RootsOverride = () => new[] { Path.Combine(AppContext.BaseDirectory, "assets", "Chaos") };
        var fx = new ChaosHubLogoFx { Width = 314, Height = 224 };
        var parent = new Border { Child = fx };
        var host = new Window { Width = 314, Height = 224, Content = parent };
        try
        {
            CoreSettings.Current.ChaosSkiaFxEnabled = true;
            Assert.DoesNotContain(BeatLoop.Running, l => ReferenceEquals(l.Owner, fx));
            host.Show();
            Dispatcher.UIThread.RunJobs();
            var loop = Assert.Single(BeatLoop.Running, l => ReferenceEquals(l.Owner, fx));
            Assert.True(loop.OnFrameBeat);
            Assert.Equal(TimeSpan.FromSeconds(1.0 / 30), loop.Interval);
            time.Advance(TimeSpan.FromMilliseconds(34));
            BeatLoop.StepAll();
            Assert.Equal(1, fx.TwinkleCount);

            parent.IsVisible = false;
            Assert.False(loop.IsTicking);
            time.Advance(TimeSpan.FromSeconds(2));
            BeatLoop.StepAll();
            Assert.Equal(1, fx.TwinkleCount);
            parent.IsVisible = true;
            Assert.True(loop.OnFrameBeat);
            host.WindowState = WindowState.Minimized;
            Assert.False(loop.IsTicking);
            host.WindowState = WindowState.Normal;
            Assert.True(loop.IsTicking);
            parent.Child = null;
            Assert.False(loop.IsRunning);
            Assert.DoesNotContain(BeatLoop.Running, l => ReferenceEquals(l.Owner, fx));
            parent.Child = fx;
            Assert.Same(loop, Assert.Single(BeatLoop.Running, l => ReferenceEquals(l.Owner, fx)));
            Assert.True(loop.OnFrameBeat);
            host.Close();
            Assert.False(loop.IsRunning);
            Assert.DoesNotContain(BeatLoop.Running, l => ReferenceEquals(l.Owner, fx));
        }
        finally
        {
            host.Close();
            BeatLoop.Time = previousTime;
            CoreSettings.Current.ChaosSkiaFxEnabled = enabled;
            ChaosArt.ResetForTest();
        }
    });

    [Fact]
    public void Mask_glow_renders_only_when_fx_enabled() => AvaloniaTestDispatcher.Run(() =>
    {
        var enabled = CoreSettings.Current.ChaosSkiaFxEnabled;
        Window? host = null;
        try
        {
            ChaosArt.RootsOverride = () => new[] { Path.Combine(AppContext.BaseDirectory, "assets", "Chaos") };
            var fx = new ChaosHubLogoFx { Width = 314, Height = 224 };
            host = new Window { Width = 314, Height = 224, Background = new SolidColorBrush(Color.FromRgb(96, 96, 96)), Content = fx };
            CoreSettings.Current.ChaosSkiaFxEnabled = false;
            host.Show();
            Dispatcher.UIThread.RunJobs();
            using var off = host.CaptureRenderedFrame()!;
            var offPixels = Pixels(off);
            CoreSettings.Current.ChaosSkiaFxEnabled = true;
            fx.InvalidateVisual();
            Dispatcher.UIThread.RunJobs();
            using var on = host.CaptureRenderedFrame()!;
            var onPixels = Pixels(on);
            Assert.True(System.Linq.Enumerable.Range(0, onPixels.Length / 4)
                .Count(i => onPixels[i * 4] > offPixels[i * 4] + 3 || onPixels[i * 4 + 1] > offPixels[i * 4 + 1] + 3 || onPixels[i * 4 + 2] > offPixels[i * 4 + 2] + 3) > 100);
            Assert.Equal(0, Enumerable.Range(0, onPixels.Length / 4).Count(i =>
                onPixels[i * 4] + 2 < offPixels[i * 4] || onPixels[i * 4 + 1] + 2 < offPixels[i * 4 + 1] || onPixels[i * 4 + 2] + 2 < offPixels[i * 4 + 2]));
        }
        finally { host?.Close(); CoreSettings.Current.ChaosSkiaFxEnabled = enabled; ChaosArt.ResetForTest(); }
    });

    [Fact]
    public void Json_can_turn_off_the_authored_glow() => AvaloniaTestDispatcher.Run(() =>
    {
        var enabled = CoreSettings.Current.ChaosSkiaFxEnabled;
        var root = Path.Combine(Path.GetTempPath(), "ccp-logo-" + Guid.NewGuid());
        Window? host = null;
        try
        {
            Directory.CreateDirectory(root);
            foreach (var name in new[] { "menu_logo.png", "menu_logo_fx.png" })
                File.Copy(Path.Combine(AppContext.BaseDirectory, "assets", "Chaos", name), Path.Combine(root, name));
            File.WriteAllText(Path.Combine(root, "menu_logo_fx.json"), "{\"glow\":{\"intensity\":0},\"sheen\":{\"intensity\":0},\"twinkle\":{\"intensity\":0}}");
            ChaosArt.RootsOverride = () => new[] { root };
            CoreSettings.Current.ChaosSkiaFxEnabled = true;
            host = new Window { Width = 314, Height = 224, Background = Brushes.Black, Content = new ChaosHubLogoFx() };
            host.Show();
            Dispatcher.UIThread.RunJobs();
            using var frame = host.CaptureRenderedFrame()!;
            var pixels = Pixels(frame);
            Assert.Equal(0, Enumerable.Range(0, pixels.Length / 4).Count(i => pixels[i * 4] + pixels[i * 4 + 1] + pixels[i * 4 + 2] > 0));
        }
        finally { host?.Close(); CoreSettings.Current.ChaosSkiaFxEnabled = enabled; ChaosArt.ResetForTest(); Directory.Delete(root, true); }
    });

    private static byte[] Pixels(global::Avalonia.Media.Imaging.Bitmap frame)
    {
        var bytes = new byte[314 * 224 * 4];
        var mem = System.Runtime.InteropServices.Marshal.AllocHGlobal(bytes.Length);
        try
        {
            frame.CopyPixels(new PixelRect(0, 0, 314, 224), mem, bytes.Length, 314 * 4);
            System.Runtime.InteropServices.Marshal.Copy(mem, bytes, 0, bytes.Length);
        }
        finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(mem); }
        return bytes;
    }
}
