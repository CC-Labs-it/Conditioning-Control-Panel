using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosSkiaFxPinTests
{
    [Fact]
    public Task PinPreferenceAndVideoGateControlRaising() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var settings = CoreSettings.Current;
        var enabled = settings.ChaosSkiaFxEnabled;
        var pin = settings.ChaosPinOnTop;
        var composited = ChaosSkiaFxOverlay.Composited;
        var videoPlaying = ChaosSkiaFxOverlay.VideoPlaying;
        var raise = ChaosSkiaFxOverlay.Raise;
        bool video = false;
        int raises = 0;
        try
        {
            settings.ChaosSkiaFxEnabled = true;
            ChaosSkiaFxOverlay.Composited = () => true;
            ChaosSkiaFxOverlay.SkipPlatformChecksForTests = true;
            ChaosSkiaFxOverlay.VideoPlaying = () => video;
            ChaosSkiaFxOverlay.Raise = _ => raises++;

            settings.ChaosPinOnTop = false;
            ChaosSkiaFxOverlay.TrailDot(new Point(100, 100), 0.4);
            var unpinned = Assert.IsType<ChaosSkiaFxOverlay>(ChaosSkiaFxOverlay.Active);
            Assert.False(unpinned.Topmost);
            video = true;
            ChaosSkiaFxOverlay.TrailDot(new Point(100, 100), 0.4);
            Assert.Equal(0, raises); // unpinned stays below other applications
            ChaosSkiaFxOverlay.CloseActive();

            settings.ChaosPinOnTop = true;
            video = false;
            ChaosSkiaFxOverlay.TrailDot(new Point(100, 100), 0.4);
            var pinned = Assert.IsType<ChaosSkiaFxOverlay>(ChaosSkiaFxOverlay.Active);
            Assert.True(pinned.Topmost);
            Assert.Equal(0, raises); // WPF only reasserts when a video is playing
            video = true;
            ChaosSkiaFxOverlay.TrailDot(new Point(100, 100), 0.4);
            Assert.Equal(1, raises);
            Assert.Same(pinned, ChaosSkiaFxOverlay.Active);
        }
        finally
        {
            ChaosSkiaFxOverlay.CloseActive();
            ChaosSkiaFxOverlay.Composited = composited;
            ChaosSkiaFxOverlay.SkipPlatformChecksForTests = false;
            ChaosSkiaFxOverlay.VideoPlaying = videoPlaying;
            ChaosSkiaFxOverlay.Raise = raise;
            settings.ChaosSkiaFxEnabled = enabled;
            settings.ChaosPinOnTop = pin;
        }
        return Task.CompletedTask;
    });
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task RefusedManagedFrameClosesTheSurface(bool throws) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var settings = CoreSettings.Current;
        bool enabled = settings.ChaosSkiaFxEnabled, pin = settings.ChaosPinOnTop;
        var composited = ChaosSkiaFxOverlay.Composited;
        var managed = ChaosSkiaFxOverlay.ManagedClickThrough;
        var refused = typeof(ChaosSkiaFxOverlay).GetField("_refused", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var oldRefused = refused.GetValue(null);
        global::Avalonia.Controls.Window? attempted = null;
        try
        {
            refused.SetValue(null, false);
            settings.ChaosSkiaFxEnabled = true;
            settings.ChaosPinOnTop = false;
            ChaosSkiaFxOverlay.Composited = () => true;
            ChaosSkiaFxOverlay.SkipPlatformChecksForTests = true;
            ChaosSkiaFxOverlay.ManagedClickThrough = w =>
            {
                attempted = w;
                if (throws) throw new InvalidOperationException("native failure");
                return false;
            };
            ChaosSkiaFxOverlay.ArmCursorGlow();
            Assert.NotNull(attempted);
            Assert.False(attempted.IsVisible);
            Assert.Null(ChaosSkiaFxOverlay.Active);
            attempted = null;
            ChaosSkiaFxOverlay.ArmCursorGlow();
            Assert.Null(attempted); // do not retry a refused full-screen surface
        }
        finally
        {
            ChaosSkiaFxOverlay.CloseActive();
            refused.SetValue(null, oldRefused);
            ChaosSkiaFxOverlay.ManagedClickThrough = managed;
            ChaosSkiaFxOverlay.Composited = composited;
            ChaosSkiaFxOverlay.SkipPlatformChecksForTests = false;
            settings.ChaosSkiaFxEnabled = enabled;
            settings.ChaosPinOnTop = pin;
        }
        return Task.CompletedTask;
    });

}
