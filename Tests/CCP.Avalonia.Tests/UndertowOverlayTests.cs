using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls.Super;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.SuperCore;
using SkiaSharp;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Lane L7 of the Linux Super port: the Undertow lens (WPF BrainDrainLayer's Undertow half),
/// stepped by Core <see cref="UndertowLens"/> and drawn by Core <see cref="ConditioningControlPanel.Services.Super.Draw.UndertowDraw"/>.
/// The Brain Drain blur itself is not on this head, so each test stands in a blur with <see cref="UndertowOverlay.BlurShowing"/>.</summary>
public sealed class UndertowOverlayTests
{
    private static bool _down;

    private static Task With(bool premium, bool blur, Action<AppSettings> body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current == null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var provider = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        var saved = (CoreEntitlement.HasPremiumProvider, SuperAccess.TryingProvider, SuperStageHost.CursorProvider,
            SuperStageHost.ClickProvider, UndertowOverlay.BlurShowing, UndertowOverlay.WavesOn);
        var all = Environment.GetEnvironmentVariable("CCP_SUPER_ALL");
        try
        {
            Environment.SetEnvironmentVariable("CCP_SUPER_ALL", null);   // the gate as shipped
            var s = service.Current;
            s.BrainDrainEnabled = true;
            s.SuperModes = SuperModeRule.With(s.SuperModes, SuperEffect.Undertow, SuperMode.Both);
            (s.MotionLevel, s.GentleVisuals) = (MotionLevel.Full, false);
            CoreSettings.ServiceProvider = () => service;
            CoreEntitlement.HasPremiumProvider = () => premium;
            SuperAccess.TryingProvider = () => null;
            SuperStageHost.CursorProvider = () => (400, 300);   // never the desk's real mouse
            _down = false;
            SuperStageHost.ClickProvider = () => (_down, false);
            UndertowOverlay.BlurShowing = () => blur;
            UndertowOverlay.Stop();
            body(s);
        }
        finally
        {
            UndertowOverlay.Stop();
            (CoreEntitlement.HasPremiumProvider, SuperAccess.TryingProvider, SuperStageHost.CursorProvider,
                SuperStageHost.ClickProvider, UndertowOverlay.BlurShowing, UndertowOverlay.WavesOn) = saved;
            Environment.SetEnvironmentVariable("CCP_SUPER_ALL", all);
            CoreSettings.ServiceProvider = provider;
        }
        return Task.CompletedTask;
    });

    private static void Run(int frames) { for (int i = 0; i < frames; i++) UndertowOverlay.Step(1 / 30.0, 800); }

    /// <summary>An 800x600 monitor whose blurred frame is opaque grey at a quarter size.</summary>
    private static UndertowCanvas Canvas()
    {
        var c = new UndertowCanvas(new PixelRect(0, 0, 800, 600), 1) { Alpha = 255 };
        using var surface = SKSurface.Create(new SKImageInfo(200, 150, SKColorType.Bgra8888, SKAlphaType.Premul));
        surface.Canvas.Clear(new SKColor(0x80, 0x80, 0x80));
        c.SetFrame(surface.Snapshot());
        return c;
    }

    private static SKBitmap Drawn(UndertowCanvas c)
    {
        using var surface = SKSurface.Create(new SKImageInfo(800, 600, SKColorType.Bgra8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Transparent);
        Assert.True(c.Draw(surface.Canvas));
        using var img = surface.Snapshot();
        return SKBitmap.FromImage(img);
    }

    [Fact]
    public Task A_free_account_keeps_the_plain_blur() => With(premium: false, blur: true, _ =>
    {
        Run(30);
        Assert.False(UndertowOverlay.On);
        Assert.False(UndertowOverlay.State.Placed);
        using var px = Drawn(Canvas());
        Assert.Equal(255, px.GetPixel(400, 300).Alpha);   // the blur under the cursor, no lens
    });

    [Fact]
    public Task Without_a_blur_there_is_no_lens_and_no_star() => With(premium: true, blur: false, _ =>
    {
        Run(30);
        Assert.False(UndertowOverlay.On);
        Assert.DoesNotContain(SuperEffect.Undertow, SuperBox.HostedOnThisHead);
    });

    [Fact]
    public Task The_lens_clears_the_blur_under_the_cursor() => With(premium: true, blur: true, _ =>
    {
        Run(30);
        Assert.True(UndertowOverlay.On);
        Assert.True(UndertowOverlay.State.Placed);
        Assert.True(UndertowOverlay.State.Radius >= UndertowOptions.StockMinRadius * 800 - 0.01);
        using var px = Drawn(Canvas());
        Assert.Equal(0, px.GetPixel(400, 300).Alpha);    // sharp under the cursor
        Assert.Equal(255, px.GetPixel(5, 5).Alpha);      // blur far from it
    });

    [Fact]
    public Task A_click_widens_the_lens_and_the_waves_stay_off() => With(premium: true, blur: true, _ =>
    {
        Run(30);
        double kick = UndertowOverlay.State.Kick;
        _down = true;
        Run(1);
        Assert.True(UndertowOverlay.State.Ripples.Any(r => r.Live));
        Assert.True(UndertowOverlay.State.Kick > kick);
        Assert.False(UndertowOverlay.WavesOn);
        var c = Canvas();
        using (Drawn(c)) { }
        Assert.False(c.DrewWave);
        // The finding: this head's SkiaSharp (3.119) compiles the src.eval shader WPF's 2.88 refuses,
        // and the path works behind the switch; it stays off for parity with WPF as shipped.
        Assert.NotNull(UndertowOverlay.WaveEffect());
        UndertowOverlay.WavesOn = true;
        using (Drawn(c)) { }
        Assert.True(c.DrewWave);
    });

    [Fact]
    public Task Panic_takes_the_lens_down_at_once() => With(premium: true, blur: true, _ =>
    {
        Run(30);
        _down = true;
        Run(1);
        Assert.True(UndertowOverlay.On);
        PanicSurfaces.All.Single(p => p.Id == "undertow").Stop(null);
        Assert.False(UndertowOverlay.On);
        Assert.False(UndertowOverlay.State.Placed);
        Assert.DoesNotContain(UndertowOverlay.State.Ripples, r => r.Live);
        Assert.DoesNotContain(UndertowOverlay.State.Drops, d => d.Live);
    });

    [Fact]
    public void The_render_sample_draws_a_lens() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var layer = (Control)Activator.CreateInstance(typeof(UndertowCanvas), nonPublic: true)!;
        layer.Measure(new Size(1280, 720));
        layer.Arrange(new Rect(0, 0, 1280, 720));
        using var bmp = new RenderTargetBitmap(new PixelSize(1280, 720));
        bmp.Render(layer);
        var px = new byte[1280 * 720 * 4];
        var buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(px.Length);
        try
        {
            bmp.CopyPixels(new PixelRect(0, 0, 1280, 720), buffer, px.Length, 1280 * 4);
            System.Runtime.InteropServices.Marshal.Copy(buffer, px, 0, px.Length);
        }
        finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer); }
        byte Alpha(int x, int y) => px[(y * 1280 + x) * 4 + 3];
        Assert.True(Alpha(640, 360) < 10, "the lens is clear under its centre");
        Assert.True(Alpha(20, 20) > 150, "the stand-in blur draws outside the lens");
    });
}
