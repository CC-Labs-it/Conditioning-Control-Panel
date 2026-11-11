using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls.Billboard;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Billboard;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class BillboardCtaTests
{
    [Fact]
    public Task CtaBreathShineHoverPressAndGates() => WithCta((host, window) =>
    {
        Assert.True(host.CtaLoopsRunning);
        host.StepCtaFx(0); Assert.Equal(0.32, host.CtaGlowOpacity, 5);
        host.StepCtaFx(1.6); Assert.Equal(0.62, host.CtaGlowOpacity, 5);
        Assert.Equal(1, host.CtaShineOpacity);
        host.StepCtaFx(3.2); Assert.Equal(0.32, host.CtaGlowOpacity, 5);
        Assert.Equal(0, host.CtaShineOpacity);
        host.Cta.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, host.Cta,
            new Pointer(1, PointerType.Mouse, true), host.Cta, default, 0, default, default));
        host.StepCtaFx(3.5);
        Assert.Equal(1.05, host.CtaPose.Hover, 5);
        Assert.Equal(0.95, host.CtaGlowOpacity, 5);
        Assert.InRange(Math.Abs(host.CtaShineX), 0, 0.01); // half-way across the real face
        host.SetCtaPressed(true); host.StepCtaFx(3.59);
        Assert.Equal(1.07, host.CtaPose.X, 5); Assert.Equal(0.86, host.CtaPose.Y, 5);
        Assert.Equal(2, host.CtaPose.Travel, 5);
        host.SetCtaPressed(false); host.SetCtaPressed(false); // capture lost after release must preserve the spring
        host.StepCtaFx(3.674);
        Assert.Equal(-1, host.CtaPose.Travel, 4);
        host.StepCtaFx(3.6908);
        Assert.Equal(0.95, host.CtaPose.X, 4); Assert.Equal(1.07, host.CtaPose.Y, 4);
        host.StepCtaFx(3.9); Assert.Equal(1, host.CtaPose.X, 5);
        host.SetCtaHover(false); host.StepCtaFx(4.2);
        CoreSettings.Current.MotionLevel = MotionLevel.Reduced; host.RefreshMotion();
        host.StepCtaFx(5);
        Assert.False(host.CtaLoopsRunning); Assert.Equal(0.32, host.CtaGlowOpacity, 5);
        host.SetCtaHover(true); host.StepCtaFx(5.3);
        Assert.Equal(1.05, host.CtaPose.Hover, 5); // interaction still allowed
        CoreSettings.Current.MotionLevel = MotionLevel.Off; host.RefreshMotion();
        Assert.False(host.CtaLoopsRunning); Assert.Equal((1d, 1d, 1d, -2d), host.CtaPose);
        host.SetCtaPressed(true); Assert.Equal((1d, 1d, 1d, 2d), host.CtaPose);
        host.SetCtaPressed(false); Assert.Equal((1d, 1d, 1d, 0d), host.CtaPose);
        host.RefreshMotion(); Assert.Equal(0, host.CtaPose.Travel); // release holds at rest
        host.SetCtaHover(false); host.SetCtaHover(true); Assert.Equal(-2, host.CtaPose.Travel);
        CoreSettings.Current.MotionLevel = MotionLevel.Full; host.RefreshMotion();
        Assert.True(host.CtaLoopsRunning); Assert.Equal(-2, host.CtaPose.Travel);
        host.Advance(); host.StepCtaFx(5.3);
        Assert.InRange(host.CtaGlowOpacity, 0.32, 0.62); // still breathing: WPF Fx.cs:115 the flare waits for the thud
        host.StepCtaFx(5.3 + (280 + 340 * 0.55) / 1000);
        Assert.Equal(0.95, host.CtaGlowOpacity, 5); // landing flare
        host.StepCtaFx(6.1 + (280 + 340 * 0.55) / 1000); // the 0.7 s flare is over
        CoreSettings.Current.PerformanceMode = true; host.RefreshMotion();
        Assert.False(host.CtaLoopsRunning);
        CoreSettings.Current.PerformanceMode = false; host.RefreshMotion();
        Assert.True(host.CtaLoopsRunning);
        host.IsVisible = false; host.RefreshMotion(); Assert.False(host.CtaLoopsRunning);
        host.IsVisible = true; host.RefreshMotion(); Assert.True(host.CtaLoopsRunning);
        window.Close(); Assert.False(host.CtaLoopsRunning);
        host.SetCtaHover(true); Assert.False(host.CtaLoopsRunning);
    }, stepInitial: false);

    [Fact]
    public Task HoverAfterReleaseDoesNotReplaySpring() => WithCta((host, window) =>
    {
        host.SetCtaHover(true); host.StepCtaFx(0.5);
        host.SetCtaPressed(true); host.StepCtaFx(0.59);
        host.SetCtaPressed(false); host.StepCtaFx(0.9);
        host.SetCtaHover(false); host.StepCtaFx(1.1);
        host.SetCtaHover(true); host.StepCtaFx(1.4);
        Assert.Equal((1.05, 1d, 1d, -2d), host.CtaPose);
        // A hover during a release must not rewind its squash clock either.
        host.SetCtaPressed(true); host.StepCtaFx(1.49);
        host.SetCtaPressed(false); host.StepCtaFx(1.5152);
        host.SetCtaHover(false); host.StepCtaFx(1.5404);
        Assert.Equal(0.98, host.CtaPose.X, 5); // half the first quadratic segment
    });

    [Fact]
    public Task RoutedPointerExitReleasesHeldPress() => WithCta((host, window) =>
    {
        var pointer = new Pointer(4, PointerType.Mouse, true);
        host.Cta.RaiseEvent(new PointerPressedEventArgs(host.Cta, pointer, host.Cta, default, 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), default, 1));
        host.StepCtaFx(0.1); Assert.Equal(0.86, host.CtaPose.Y, 5);
        host.Cta.RaiseEvent(new PointerEventArgs(InputElement.PointerExitedEvent, host.Cta, pointer,
            host.Cta, default, 1, new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other), default));
        host.StepCtaFx(0.4);
        Assert.Equal((1d, 1d, 1d, 0d), host.CtaPose);
    });

    [Fact]
    public Task IntermediateFramesMatchWpfTimingAndQuadraticSegments() => WithCta((host, window) =>
    {
        host.StepCtaFx(0); host.SetCtaHover(true); host.StepCtaFx(0.08);
        Assert.Equal(0.635, host.CtaGlowOpacity, 5); // linear 160 ms glow fade
        host.StepCtaFx(0.13); Assert.InRange(host.CtaPose.Hover, 1.05, 1.075); // WPF thud overshoots
        host.StepCtaFx(0.5); host.SetCtaHover(false); host.StepCtaFx(0.58);
        Assert.Equal(1.0125, host.CtaPose.Hover, 5); // half of 160 ms, quadratic Out
        Assert.Equal(0.95 + (0.32 - 0.95) * (0.08 / 0.26), host.CtaGlowOpacity, 5);
        host.StepCtaFx(0.66); Assert.Equal(1, host.CtaPose.Hover, 5);
        host.SetCtaPressed(true); host.StepCtaFx(0.75); host.SetCtaPressed(false);
        host.StepCtaFx(0.7752); Assert.Equal(1.0175, host.CtaPose.X, 5); // first segment at 25 percent
        host.StepCtaFx(0.792); Assert.Equal(-0.25, host.CtaPose.Travel, 5);
        host.StepCtaFx(0.8816); Assert.Equal(0.95625, host.CtaPose.X, 5); // final segment at 25 percent
    });

    [Fact]
    public Task SustainedHoverShinesOnceAndHoldsAtTheEnd() => WithCta((host, window) =>
    {
        host.StepCtaFx(3.2); host.SetCtaHover(true);
        host.StepCtaFx(3.5);
        Assert.Equal(1, host.CtaShineOpacity);
        Assert.InRange(Math.Abs(host.CtaShineX), 0, 0.01);
        host.StepCtaFx(3.9);
        Assert.Equal(0, host.CtaShineOpacity);
        var end = host.CtaShineX;
        Assert.True(end > 0);
        foreach (var t in new[] { 6.1, 10.7, 15.3 })
        {
            host.StepCtaFx(t);
            Assert.Equal(0, host.CtaShineOpacity);
            Assert.Equal(end, host.CtaShineX, 5);
        }
    });

    [Fact]
    public Task HoverExitRestartsThePeriodicShineDelay() => WithCta((host, window) =>
    {
        host.StepCtaFx(3.2); host.SetCtaHover(true); host.StepCtaFx(6.1);
        host.SetCtaHover(false);
        foreach (var t in new[] { 6.1, 6.4, 7.299 })
        {
            host.StepCtaFx(t);
            Assert.Equal(0, host.CtaShineOpacity); // wait 1.2 s from exit, not the original card epoch
        }
        host.StepCtaFx(7.6);
        Assert.Equal(1, host.CtaShineOpacity);
        Assert.InRange(Math.Abs(host.CtaShineX), 0, 0.01); // half-way through the first resumed sweep
        host.StepCtaFx(7.91); Assert.Equal(0, host.CtaShineOpacity);
        host.StepCtaFx(12.2); // the next sweep is 4.6 s later
        Assert.Equal(1, host.CtaShineOpacity);
        Assert.InRange(Math.Abs(host.CtaShineX), 0, 0.01);
    });

    [Fact]
    public Task CtaShineLeavesOpaqueLabelPixelsUnchanged() => WithCta((host, window) =>
    {
        host.SetCtaHover(true); host.StepCtaFx(0.3); // the sweep is centred across the label
        var shine = host.Cta.GetLogicalDescendants().OfType<global::Avalonia.Controls.Shapes.Rectangle>().Single();
        int[] Pixels()
        {
            int w = (int)Math.Ceiling(host.Cta.Bounds.Width), h = (int)Math.Ceiling(host.Cta.Bounds.Height);
            using var bitmap = new global::Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(w, h));
            bitmap.Render(host.Cta);
            var buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(w * h * 4);
            try
            {
                bitmap.CopyPixels(new PixelRect(0, 0, w, h), buffer, w * h * 4, w * 4);
                var pixels = new int[w * h];
                System.Runtime.InteropServices.Marshal.Copy(buffer, pixels, 0, pixels.Length);
                return pixels;
            }
            finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer); }
        }
        shine.IsVisible = false;
        var plain = Pixels();
        shine.IsVisible = true;
        var lit = Pixels();
        var ink = Enumerable.Range(0, plain.Length).Where(i => unchecked((uint)plain[i]) == 0xff0d0e1c).ToArray();
        Assert.True(ink.Length >= 5, "The rendered label must contain opaque ink pixels.");
        foreach (var i in ink) Assert.Equal(plain[i], lit[i]);
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CtaFixturesRestoreSettingsProviderBeforeTheIsolationHook(bool mainFixture)
    {
        var old = CoreSettings.ServiceProvider;
        if (mainFixture) await CtaBreathShineHoverPressAndGates();
        else await WithCta((host, window) => { });
        Assert.Same(old, CoreSettings.ServiceProvider);
        if (!mainFixture)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => WithCta((host, window) => throw new InvalidOperationException("fixture probe")));
            Assert.Same(old, CoreSettings.ServiceProvider);
        }
    }

    private Task WithCta(Action<BillboardCardHost, Window> check, bool stepInitial = true) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var old = CoreSettings.ServiceProvider;
        CoreSettings.ServiceProvider = () => settings;
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        CoreSettings.Current.PerformanceMode = CoreSettings.Current.AutoPerformanceMode = false;
        var deck = new BillboardDeck(() => new[] { new Cards() },
            () => new BillboardContext(BillboardTier.Free, DateTime.UtcNow, DateTime.Now), new Dictionary<string, DateTime>(), () => { });
        var host = new BillboardCardHost(deck);
        var window = new Window { Width = 640, Height = 320, Content = host, FontFamily = new FontFamily("DejaVu Sans") };
        try
        {
            host.Begin();
            foreach (var text in host.GetLogicalDescendants().OfType<TextBlock>()) text.FontFamily = window.FontFamily;
            window.Show(); Dispatcher.UIThread.RunJobs(); if (stepInitial) host.StepCtaFx(0);
            check(host, window);
        }
        finally
        {
            try { window.Close(); host.ReleaseArt(); }
            finally { CoreSettings.ServiceProvider = old; }
        }
        return Task.CompletedTask;
    });

    private readonly SettingsService settings = new();
    private sealed class Cards : IBillboardProvider
    {
        public string Id => "test";
        public IEnumerable<BillboardCardSpec> Current(BillboardContext c)
        {
            var card = new BillboardCardSpec("cta", BillboardCardKind.House, 0, "", "Card", "", "#ff4fa8", "", null,
                new BillboardAction(BillboardActionKind.Tab, "play", "Open"));
            return new[] { card, card with { Id = "cta2", Priority = 1 } };
        }
        public void Invoke(string target) { }
        public event EventHandler? Changed { add { } remove { } }
    }
}
