using System;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class Sync7bGentleTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().WithInterFont()
                .With(ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(ConditioningControlPanel.Avalonia.Platform.AppFonts.Register).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private sealed class SettingsScope : IDisposable
    {
        private readonly Action _restore;
        public SettingsScope()
        {
            var provider = CoreSettings.ServiceProvider;
            var s = CoreSettings.Current;
            var values = (s.GentleVisuals, s.GentleFadeSeconds, s.GentleSpeed, s.MotionLevel,
                s.SubBackgroundTransparent, s.FlashExitStyle, s.CorruptionMode, s.FlashShatterEnabled);
            _restore = () =>
            {
                try
                {
                    (s.GentleVisuals, s.GentleFadeSeconds, s.GentleSpeed, s.MotionLevel,
                        s.SubBackgroundTransparent, s.FlashExitStyle, s.CorruptionMode, s.FlashShatterEnabled) = values;
                }
                finally { CoreSettings.ServiceProvider = provider; }
            };
        }
        public void Dispose() => _restore();
    }

    [Fact]
    public void ClockTestRestoresItsProviderAndSettings()
    {
        using var saved = new SettingsScope();
        var provider = CoreSettings.ServiceProvider;
        var s = CoreSettings.Current;
        var before = (s.GentleVisuals, s.GentleFadeSeconds, s.GentleSpeed);
        GentleClocksLengthenSubliminalEnvelopeAndSpiralFrameDelay(true);
        Assert.Same(provider, CoreSettings.ServiceProvider);
        Assert.Equal(before, (s.GentleVisuals, s.GentleFadeSeconds, s.GentleSpeed));
        Assert.Throws<InvalidOperationException>(new Action(() =>
        {
            using var inner = new SettingsScope();
            s.GentleVisuals = !s.GentleVisuals;
            s.GentleSpeed = 0.25;
            CoreSettings.ServiceProvider = () => null;
            throw new InvalidOperationException();
        }));
        Assert.Same(provider, CoreSettings.ServiceProvider);
        Assert.Equal(before, (s.GentleVisuals, s.GentleFadeSeconds, s.GentleSpeed));
    }

    private sealed class SteppedClock : TimeProvider
    {
        public long Now;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }

    [Fact]
    public Task GentleMidFadePopKeepsCurrentBrightness() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        using var saved = new SettingsScope();
        EnsureApp();
        CoreSettings.Current.GentleVisuals = true;
        CoreSettings.Current.GentleFadeSeconds = 1.5;
        CoreSettings.Current.CorruptionMode = false;
        var oldClock = FlashOverlay.Clock;
        var clock = new SteppedClock();
        FlashOverlay.Clock = clock;
        var w = new FlashOverlayWindow();
        FlashOverlay.Active.Add((w, new PixelRect(0, 0, 400, 300)));
        try
        {
            w.Show();
            w.Run(0.8, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(20));
            var wait = System.Diagnostics.Stopwatch.StartNew();
            while (w.DisplayedAlpha == 0 && wait.Elapsed < TimeSpan.FromSeconds(2))
                await Task.Delay(20);
            var before = w.DisplayedAlpha;
            Assert.InRange(before, 0.001, 0.799);
            FlashOverlay.Pop(w);
            Assert.Equal(before, w.DisplayedAlpha, 10);
            clock.Now += TimeSpan.TicksPerSecond * 3 / 4;
            w.ExitFrame();
            Assert.Equal(before / 2, w.DisplayedAlpha, 10);
            await Task.Delay(150); // queued fade-in callbacks must not brighten the popped flash
            Assert.Equal(before / 2, w.DisplayedAlpha, 10);
            clock.Now += TimeSpan.TicksPerSecond;
            w.ExitFrame();
            Assert.False(w.IsVisible);
        }
        finally { FlashOverlay.CloseAll(final: false); w.Close(); FlashOverlay.Clock = oldClock; }
    });

    [Fact]
    public Task GentlePopUsesPlainFadeEvenWhenExitIsNone() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        using var saved = new SettingsScope();
        EnsureApp();
        var s = CoreSettings.Current;
        s.GentleVisuals = true;
        s.GentleFadeSeconds = 1.5;
        s.FlashExitStyle = FlashExitStyle.None;
        s.CorruptionMode = false;
        s.FlashShatterEnabled = true;
        var w = new FlashOverlayWindow();
        FlashOverlay.Active.Add((w, new PixelRect(0, 0, 400, 300)));
        try
        {
            w.Show();
            FlashOverlay.Pop(w);
            Assert.DoesNotContain(FlashOverlay.Active, e => e.Window == w);
            Assert.Contains(w, FlashOverlay.Exiting);
            Assert.NotNull(w.Exit);
            Assert.Null(w.Shatter);
            Assert.True(w.Exit.Reduced);
            Assert.Equal(1.5, w.Exit.DurationSec);
            var sample = ConditioningControlPanel.Services.Flash.FlashExit.Sample(w.Exit);
            Assert.Equal(1, sample.ScaleX);
            Assert.Equal(1, sample.ScaleY);
        }
        finally { FlashOverlay.CloseAll(final: false); w.Close(); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GentleClocksLengthenSubliminalEnvelopeAndSpiralFrameDelay(bool spiral)
    {
        using var saved = new SettingsScope();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = service.Current;
        s.GentleVisuals = false;
        var normal = SpiralOverlay.FrameDelay;
        Assert.Equal(50, SubliminalOverlayWindow.FadeDuration.TotalMilliseconds);
        Assert.Equal(300, SubliminalOverlayWindow.MaxLifetime(TimeSpan.FromMilliseconds(100)).TotalMilliseconds);
        s.GentleVisuals = true;
        s.GentleFadeSeconds = 1.5;
        s.GentleSpeed = 0.25;
        if (spiral) Assert.Equal(normal * 4, SpiralOverlay.FrameDelay);
        else
        {
            Assert.Equal(1500, SubliminalOverlayWindow.FadeDuration.TotalMilliseconds);
            Assert.Equal(3200, SubliminalOverlayWindow.MaxLifetime(TimeSpan.FromMilliseconds(100)).TotalMilliseconds);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task GentleCardsDropBackgroundAndCornerRings(bool rings) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        using var saved = new SettingsScope();
        EnsureApp();
        var s = CoreSettings.Current;
        s.SubBackgroundTransparent = false;
        s.GentleVisuals = true;
        if (!rings)
        {
            Assert.Empty(SubliminalOverlayWindow.Build("quiet", s).Children.OfType<Rectangle>());
            s.GentleVisuals = false;
            Assert.Single(SubliminalOverlayWindow.Build("quiet", s).Children.OfType<Rectangle>());
            return Task.CompletedTask;
        }
        var w = new BouncingTextOverlayWindow(new PixelRect(0, 0, 1000, 800), 1, 72, 100, 0, false);
        try
        {
            w.SpawnBurst(100, 100, Colors.Pink);
            Assert.Empty(w.Canvas.Children);
            s.GentleVisuals = false;
            w.SpawnBurst(100, 100, Colors.Pink);
            Assert.Single(w.Canvas.Children);
        }
        finally { w.Close(); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task IncompleteGentlePromiseStaysOutOfTheSettingsUi(bool enabled) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        using var saved = new SettingsScope();
        EnsureApp();
        var s = CoreSettings.Current;
        s.GentleVisuals = enabled;
        var section = new PerformanceSettingsSection();
        section.OnSectionShown();
        // Every control, including the tooltip's grid, must have an invisible ancestor.
        // A saved preference or a settings refresh must never reveal the pending promise.
        var toggle = section.FindControl<CheckBox>("ChkGentleVisuals")!;
        var choices = section.FindControl<StackPanel>("PnlGentleChoices")!;
        Assert.Equal(enabled, toggle.IsChecked);
        Assert.Equal(enabled, choices.IsVisible);
        var controls = section.GetLogicalDescendants().OfType<Control>()
            .Where(c => c.Name is "ChkGentleVisuals" or "CmbGentleFade" or "CmbGentleSpeed"
                || ToolTip.GetTip(c) is not null && c.GetLogicalDescendants().Contains(toggle)).ToArray();
        Assert.Equal(4, controls.Length);
        foreach (var control in controls)
            Assert.Contains(control.GetLogicalAncestors().OfType<Control>(), ancestor => !ancestor.IsVisible);
        Assert.Equal(enabled, s.GentleVisuals);
        return Task.CompletedTask;
    });

    [Fact]
    public Task GentleControlsSeedWithoutRoundingAndRoundTripWithoutChangingMotion() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        using var saved = new SettingsScope();
        EnsureApp();
        var s = CoreSettings.Current;
        s.GentleVisuals = false;
        s.GentleFadeSeconds = 0.6;
        s.GentleSpeed = 0.4;
        s.MotionLevel = MotionLevel.Full;
        var section = new PerformanceSettingsSection();
        var toggle = section.FindControl<CheckBox>("ChkGentleVisuals")!;
        var choices = section.FindControl<StackPanel>("PnlGentleChoices")!;
        var fade = section.FindControl<ComboBox>("CmbGentleFade")!;
        var speed = section.FindControl<ComboBox>("CmbGentleSpeed")!;
        Assert.False(choices.IsVisible);
        Assert.Equal(0.6, s.GentleFadeSeconds);
        Assert.Equal(0.4, s.GentleSpeed);
        Assert.Equal(1, fade.SelectedIndex);
        Assert.Equal(2, speed.SelectedIndex);
        var gates = 0;
        void Changed() => gates++;
        AmbientFxCanvas.Env.MotionGateChanged += Changed;
        try
        {
            toggle.IsChecked = true;
            Assert.True(s.GentleVisuals);
            Assert.True(choices.IsVisible);
            Assert.Equal(1, gates);
            Assert.Equal(MotionLevel.Reduced, AmbientFxCanvas.Env.Level);
            Assert.Equal(MotionLevel.Full, s.MotionLevel);
            fade.SelectedIndex = 4;
            speed.SelectedIndex = 3;
            Assert.Equal(1.5, s.GentleFadeSeconds);
            Assert.Equal(0.25, s.GentleSpeed);
            toggle.IsChecked = false;
            Assert.False(s.GentleVisuals);
            Assert.False(choices.IsVisible);
            Assert.Equal(1.5, s.GentleFadeSeconds);
            Assert.Equal(0.25, s.GentleSpeed);
        }
        finally { AmbientFxCanvas.Env.MotionGateChanged -= Changed; }
        return Task.CompletedTask;
    });
}
