using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class GentleLayersTests
{
    private static Task OnUi(Action<AppSettings> test) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var previous = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var writer = GentleOverlayFade.WriteOpacity;
        try { test(service.Current); }
        finally
        {
            GentleOverlayFade.CloseAll();
            FlashOverlay.CloseAll(final: false);
            GentleOverlayFade.WriteOpacity = writer;
            CoreSettings.ServiceProvider = previous;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Overlay_show_hide_and_panic_keep_the_fade_bounded() => OnUi(s =>
    {
        s.GentleVisuals = true;
        s.GentleFadeSeconds = 1;
        var w = new TintOverlayWindow();
        var alpha = -1.0;
        GentleOverlayFade.WriteOpacity = (_, a) => alpha = a;
        GentleOverlayFade.Show(w);
        Assert.Equal(0, alpha);
        w.Show();
        GentleOverlayFade.Advance(TimeSpan.FromSeconds(.5));
        Assert.Equal(.5, alpha, 6);
        GentleOverlayFade.Close(w);
        GentleOverlayFade.Advance(TimeSpan.FromSeconds(.25));
        Assert.Equal(.25, alpha, 6);
        Assert.True(w.IsVisible);
        GentleOverlayFade.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(0, alpha);
        Assert.False(w.IsVisible);

        var pending = new TintOverlayWindow();
        pending.Show();
        GentleOverlayFade.Close(pending);
        PanicSurfaces.All.Single(x => x.Id == "gentle-fades").Stop(null);
        Assert.False(pending.IsVisible);
    });

    [Fact]
    public Task Switching_gentle_off_finishes_a_pending_hide_and_off_shows_unchanged() => OnUi(s =>
    {
        s.GentleVisuals = false;
        var w = new TintOverlayWindow();
        GentleOverlayFade.Show(w);
        Assert.Equal(1, w.Opacity);
        w.Show();
        s.GentleVisuals = true;
        GentleOverlayFade.Close(w);
        s.GentleVisuals = false;
        GentleOverlayFade.Advance(TimeSpan.Zero);
        Assert.False(w.IsVisible);
    });

    [Fact]
    public Task Subliminal_keeps_hold_but_lengthens_both_fades_and_removes_card() => OnUi(s =>
    {
        s.SubBackgroundTransparent = false;
        s.GentleVisuals = false;
        Assert.Equal(10, SubliminalOverlayWindow.Build("WORDS", s).Children.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(50), SubliminalOverlayWindow.FadeLength);
        s.GentleVisuals = true;
        s.GentleFadeSeconds = 1.5;
        Assert.Equal(9, SubliminalOverlayWindow.Build("WORDS", s).Children.Count);
        Assert.Equal(TimeSpan.FromSeconds(1.5), SubliminalOverlayWindow.FadeLength);
        Assert.Equal(TimeSpan.FromMilliseconds(3300), SubliminalOverlayWindow.MaxLifetime(TimeSpan.FromMilliseconds(200)));
    });

    [Fact]
    public Task Spiral_delay_scales_and_gentle_pop_overrides_even_None() => OnUi(s =>
    {
        s.GentleVisuals = false;
        var normal = SpiralOverlay.FrameDelay;
        s.GentleVisuals = true;
        s.GentleSpeed = .25;
        s.GentleFadeSeconds = 1.5;
        Assert.Equal(normal * 4, SpiralOverlay.FrameDelay);
        s.FlashExitStyle = FlashExitStyle.None;
        s.CorruptionMode = false;
        var w = new FlashOverlayWindow();
        w.Show();
        FlashOverlay.Active.Add((w, new PixelRect(0, 0, 100, 100)));
        w.Run(.5, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        Assert.Equal(TimeSpan.FromSeconds(1.5), w.FadeLength);
        FlashOverlay.Pop(w);
        Assert.Empty(FlashOverlay.Active);
        Assert.Contains(w, FlashOverlay.Exiting);
        Assert.NotNull(w.Exit);
        Assert.Equal(FlashExitStyle.Mix, w.Exit.Style); // WPF Gentle uses the plain-fade sentinel
        Assert.True(w.Exit.Reduced);
        Assert.Equal(1.5, w.Exit.DurationSec);
        Assert.Null(w.Sparks);
        Assert.Null(w.Slices);
        Assert.True(w.IsVisible);
    });
}
