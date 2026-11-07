using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Rows the WPF feature pages carry that this head was missing (parity pass 2026-11-06).</summary>
public sealed class FeatureParityRowsTests
{
    private static void Boot()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    // ChaosDvdOverlayTests.MakeScreen: Screen has no public constructor.
    private static global::Avalonia.Platform.Screen MakeScreen(PixelRect bounds)
    {
        var screen = (global::Avalonia.Platform.Screen)Activator.CreateInstance(typeof(global::Avalonia.Platform.PlatformScreen),
            new object[] { new global::Avalonia.Platform.PlatformHandle(new IntPtr(bounds.X + 1), "headless-test") })!;
        typeof(global::Avalonia.Platform.Screen).GetProperty("Scaling")!.SetValue(screen, 1.0);
        typeof(global::Avalonia.Platform.Screen).GetProperty("Bounds")!.SetValue(screen, bounds);
        typeof(global::Avalonia.Platform.Screen).GetProperty("WorkingArea")!.SetValue(screen, bounds);
        typeof(global::Avalonia.Platform.Screen).GetProperty("IsPrimary")!.SetValue(screen, true);
        return screen;
    }

    [Theory]
    [InlineData(100, 1.0, 100)]
    [InlineData(100, 2.0, 50)]
    [InlineData(100, 0.25, 400)]
    [InlineData(100, 9.0, 25)]    // clamped to 4x
    [InlineData(20, 4.0, 10)]     // never under 10 ms a frame
    [InlineData(0, 1.0, 100)]     // a GIF with no delay plays at 100 ms
    public void Gif_frame_delay_is_WPF_ScaleFrameDelay(int sourceMs, double multiplier, int expectedMs)
    {
        var gentle = CoreSettings.Current.GentleVisuals;
        try
        {
            CoreSettings.Current.GentleVisuals = false;
            Assert.Equal(expectedMs, FlashOverlay.GifFrameDelay(TimeSpan.FromMilliseconds(sourceMs), multiplier).TotalMilliseconds, 3);
        }
        finally { CoreSettings.Current.GentleVisuals = gentle; }
    }

    [Fact]
    public Task Visuals_gif_speed_row_loads_and_saves_the_multiplier() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Boot();
        var s = CoreSettings.Current;
        var saved = s.FlashGifSpeedMultiplier;
        try
        {
            s.FlashGifSpeedMultiplier = 2.5;
            var card = new VisualsFeatureControl();
            Assert.Equal(2.5, card.FindControl<Slider>("SliderGifSpeed")!.Value);
            Assert.Equal("2.5x", card.FindControl<TextBlock>("TxtGifSpeed")!.Text);
            card.FindControl<Slider>("SliderGifSpeed")!.Value = 0.5;
            Assert.Equal(0.5, s.FlashGifSpeedMultiplier);
            Assert.Equal("0.5x", card.FindControl<TextBlock>("TxtGifSpeed")!.Text);
        }
        finally { s.FlashGifSpeedMultiplier = saved; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Pink_filter_under_effects_row_round_trips_the_setting() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Boot();
        var s = CoreSettings.Current;
        var saved = s.PinkFilterUnderEffects;
        try
        {
            s.PinkFilterUnderEffects = true;
            var card = new PinkFilterFeatureControl();
            var row = card.FindControl<CheckBox>("ChkUnderEffects")!;
            Assert.True(row.IsChecked);
            row.IsChecked = false;
            Assert.False(s.PinkFilterUnderEffects);
        }
        finally { s.PinkFilterUnderEffects = saved; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Pink_filter_resort_raises_effects_when_under_and_the_tint_when_over() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Boot();
        var s = CoreSettings.Current;
        var saved = (s.PinkFilterEnabled, s.PinkFilterUnderEffects);
        var (effects, raise, skip, query) = (PinkFilterOverlay.EffectWindows, PinkFilterOverlay.RaiseWindow,
            PinkFilterOverlay.SkipPlatformChecksForTests, ScreenList.QueryOverride);
        var spiral = new Window();
        var raised = new List<Window>();
        try
        {
            PinkFilterOverlay.SkipPlatformChecksForTests = true;
            PinkFilterOverlay.EffectWindows = () => new[] { spiral };
            PinkFilterOverlay.RaiseWindow = w => { raised.Add(w); return true; };
            var host = new Window();
            host.Show();
            var screen = MakeScreen(new PixelRect(0, 0, 1920, 1080));
            ScreenList.QueryOverride = _ => new[] { screen };
            s.PinkFilterEnabled = true;
            s.PinkFilterUnderEffects = false;
            PinkFilterOverlay.Refresh(host);
            Assert.Single(PinkFilterOverlay.Shown);
            Assert.Empty(raised);   // over: a fresh tint is already on top

            s.PinkFilterUnderEffects = true;
            PinkFilterOverlay.SyncOrder();
            Assert.Equal(new[] { spiral }, raised);

            raised.Clear();
            s.PinkFilterUnderEffects = false;
            PinkFilterOverlay.SyncOrder();
            Assert.Equal(PinkFilterOverlay.Shown, raised);
            host.Close();
        }
        finally
        {
            PinkFilterOverlay.CloseAll();
            (s.PinkFilterEnabled, s.PinkFilterUnderEffects) = saved;
            (PinkFilterOverlay.EffectWindows, PinkFilterOverlay.RaiseWindow, PinkFilterOverlay.SkipPlatformChecksForTests, ScreenList.QueryOverride)
                = (effects, raise, skip, query);
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Bubble_pop_stare_to_pop_round_trips_and_hints_without_a_camera() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Boot();
        var s = CoreSettings.Current;
        var saved = s.BubbleGazePopEnabled;
        try
        {
            s.BubbleGazePopEnabled = false;
            var card = new BubblePopFeatureControl();
            var row = card.FindControl<CheckBox>("ChkBubbleGazePop")!;
            Assert.False(row.IsChecked);
            Assert.True(card.FindControl<TextBlock>("TxtBubbleGazeHint")!.IsVisible);   // no camera running in tests
            row.IsChecked = true;
            Assert.True(s.BubbleGazePopEnabled);
        }
        finally { s.BubbleGazePopEnabled = saved; }
        return Task.CompletedTask;
    });
}
