using System;
using System.IO;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using SkiaSharp;
using Xunit;

namespace CCP.Avalonia.Tests;

public class ChaosHubMenuFxTests
{
    [Fact]
    public void Masks_tuning_gate_and_lifetime() => AvaloniaTestDispatcher.Run(() =>
    {
        string root = Path.Combine(Path.GetTempPath(), "ccp-menu-fx-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        var enabled = CoreSettings.Current.ChaosSkiaFxEnabled;
        var oldTime = BeatLoop.Time;
        var time = new SteppedTime(); BeatLoop.Time = time;
        var fx = new ChaosHubMenuFx(); var parent = new Border { Child = fx };
        var host = new Window { Width = 200, Height = 200, Background = Brushes.Black, Content = parent };
        try
        {
            using (var bitmap = new SKBitmap(32, 32))
            {
                bitmap.Erase(SKColors.White);
                using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(Path.Combine(root, "menu_1_fx.png"), data.ToArray());
            }
            File.WriteAllText(Path.Combine(root, "menu_fx.json"), "{\"glow\":{\"intensity\":0},\"twinkle\":{\"frequency\":4}}");
            ChaosArt.RootsOverride = () => new[] { root };
            CoreSettings.Current.ChaosSkiaFxEnabled = false;
            host.Show(); Dispatcher.UIThread.RunJobs();
            Assert.True(fx.SpotCount > 0);
            Assert.Equal(0f, (float)typeof(ChaosHubMenuFx).GetField("_glowI", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(fx)!);
            float x = fx.FogX; fx.Step(); Assert.Equal(x, fx.FogX); Assert.Equal(0, fx.TwinkleCount);
            using var off = host.CaptureRenderedFrame()!;
            Assert.Equal(0, RgbSum(off));
            CoreSettings.Current.ChaosSkiaFxEnabled = true;
            fx.Step(); Assert.NotEqual(x, fx.FogX); Assert.Equal(1, fx.TwinkleCount);
            fx.InvalidateVisual(); Dispatcher.UIThread.RunJobs();
            using var on = host.CaptureRenderedFrame()!;
            Assert.NotEqual(PixelSum(off), PixelSum(on));
            var loop = Assert.Single(BeatLoop.Running, l => ReferenceEquals(l.Owner, fx));
            parent.IsVisible = false; Assert.False(loop.IsTicking);
            parent.Child = null; Assert.False(loop.IsRunning);
        }
        finally { host.Close(); CoreSettings.Current.ChaosSkiaFxEnabled = enabled; BeatLoop.Time = oldTime; ChaosArt.ResetForTest(); Directory.Delete(root, true); }
    });

    [Fact]
    public void Intro_eases_and_frame_fade_tracks_menu() => AvaloniaTestDispatcher.Run(() =>
    {
        var old = global::ConditioningControlPanel.Avalonia.RenderProof.Rendering;
        global::ConditioningControlPanel.Avalonia.RenderProof.Rendering = true;
        var hub = new ChaosHubWindow();
        try
        {
            var type = typeof(ChaosHubWindow);
            void Invoke(string n) => type.GetMethod(n, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(hub, null);
            void Set(string n, object v) => type.GetField(n, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(hub, v);
            var clip = hub.FindControl<Grid>("MenuArtClip")!;
            Invoke("StartMenuIntro"); Assert.Equal(0, clip.Opacity);
            Set("_menuIntroStart", Environment.TickCount64 - 550); Invoke("StepMenuMotion");
            Assert.InRange(clip.Opacity, .87, .90);
            var motion = hub.FindControl<Grid>("MenuArtMotion")!;
            double IntroScale() => ((ScaleTransform)((TransformGroup)motion.RenderTransform!).Children[0]).ScaleX;
            Assert.InRange(IntroScale(), 1.009, 1.014);
            Set("_menuIntroStart", Environment.TickCount64 - 1100); Invoke("StepMenuMotion"); Assert.Equal(1, clip.Opacity);
            Assert.Equal(1, IntroScale());
            var fx = hub.FindControl<ChaosHubMenuFx>("MenuFog")!;
            Set("_menuFading", true); Set("_menuShownFrame", 1); Set("_menuFadeStart", Environment.TickCount64 - 275);
            Invoke("StepMenuMotion");
            Assert.InRange((float)typeof(ChaosHubMenuFx).GetField("_fade", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(fx)!, .49f, .55f);
        }
        finally { hub.Close(); global::ConditioningControlPanel.Avalonia.RenderProof.Rendering = old; }
    });

    [Fact]
    public void Bloom_follows_the_frame_crossfade() => AvaloniaTestDispatcher.Run(() =>
    {
        string root = Path.Combine(Path.GetTempPath(), "ccp-menu-fade-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        var enabled = CoreSettings.Current.ChaosSkiaFxEnabled;
        var oldTime = BeatLoop.Time; BeatLoop.Time = new SteppedTime();
        var fx = new ChaosHubMenuFx();
        var host = new Window { Width = 200, Height = 200, Background = Brushes.Black, Content = fx };
        try
        {
            void Png(string name, SKColor color)
            {
                using var bitmap = new SKBitmap(32, 32); bitmap.Erase(color);
                using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(Path.Combine(root, name), data.ToArray());
            }
            Png("menu_1.png", SKColors.White); Png("menu_2.png", SKColors.White);
            Png("menu_1_fx.png", SKColors.White); Png("menu_2_fx.png", SKColors.Black);
            File.WriteAllText(Path.Combine(root, "menu_fx.json"), "{\"sheen\":{\"intensity\":0},\"twinkle\":{\"intensity\":0}}");
            ChaosArt.RootsOverride = () => new[] { root }; CoreSettings.Current.ChaosSkiaFxEnabled = true;
            host.Show(); Dispatcher.UIThread.RunJobs();
            long Frame(double fade)
            {
                fx.SetFrames(0, 1, fade); Dispatcher.UIThread.RunJobs();
                using var image = host.CaptureRenderedFrame()!; return PixelSum(image);
            }
            long bright = Frame(0), middle = Frame(.5), dark = Frame(1);
            Assert.True(bright > middle && middle > dark, $"{bright}, {middle}, {dark}");
            var fields = BindingFlags.NonPublic | BindingFlags.Instance;
            typeof(ChaosHubMenuFx).GetField("_glowI", fields)!.SetValue(fx, 0f);
            typeof(ChaosHubMenuFx).GetField("_sheenI", fields)!.SetValue(fx, 1f);
            typeof(ChaosHubMenuFx).GetField("_clock", fields)!.SetValue(fx, 0f);
            long noSheen = Frame(0);
            typeof(ChaosHubMenuFx).GetField("_clock", fields)!.SetValue(fx, .7f);
            long sheen = Frame(0);
            Assert.True(sheen > noSheen, $"{sheen}, {noSheen}");
        }
        finally { host.Close(); CoreSettings.Current.ChaosSkiaFxEnabled = enabled; ChaosArt.ResetForTest(); Directory.Delete(root, true); }
    });

    private static long RgbSum(global::Avalonia.Media.Imaging.Bitmap frame)
    {
        var size = frame.PixelSize; var bytes = new byte[size.Width * size.Height * 4];
        var mem = System.Runtime.InteropServices.Marshal.AllocHGlobal(bytes.Length);
        try { frame.CopyPixels(new PixelRect(0, 0, size.Width, size.Height), mem, bytes.Length, size.Width * 4); System.Runtime.InteropServices.Marshal.Copy(mem, bytes, 0, bytes.Length); long sum = 0; for (int i = 0; i < bytes.Length; i += 4) sum += bytes[i] + bytes[i + 1] + bytes[i + 2]; return sum; }
        finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(mem); }
    }

    private static long PixelSum(global::Avalonia.Media.Imaging.Bitmap frame)
    {
        var size = frame.PixelSize; var bytes = new byte[size.Width * size.Height * 4];
        var mem = System.Runtime.InteropServices.Marshal.AllocHGlobal(bytes.Length);
        try { frame.CopyPixels(new PixelRect(0, 0, size.Width, size.Height), mem, bytes.Length, size.Width * 4); System.Runtime.InteropServices.Marshal.Copy(mem, bytes, 0, bytes.Length); long sum = 0; foreach (byte b in bytes) sum += b; return sum; }
        finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(mem); }
    }
}
