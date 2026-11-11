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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PinPreferenceAndVideoGateControlRaising(bool x11) => AvaloniaTestDispatcher.RunAsync(() =>
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
        var needsRaise = ChaosSkiaFxOverlay.X11NeedsRaise;
        var raise = ChaosSkiaFxOverlay.Raise;
        bool video = false;
        int raises = 0;
        try
        {
            settings.ChaosSkiaFxEnabled = true;
            ChaosSkiaFxOverlay.Composited = () => true;
            ChaosSkiaFxOverlay.SkipPlatformChecksForTests = true;
            ChaosSkiaFxOverlay.VideoPlaying = () => video;
            ChaosSkiaFxOverlay.X11NeedsRaise = _ => x11;
            ChaosSkiaFxOverlay.Raise = _ => raises++;

            settings.ChaosPinOnTop = false;
            ChaosSkiaFxOverlay.TrailDot(new Point(100, 100), 0.4);
            var unpinned = Assert.IsType<ChaosSkiaFxOverlay>(ChaosSkiaFxOverlay.Active);
            Assert.False(unpinned.Topmost);
            video = true;
            ChaosSkiaFxOverlay.TrailDot(new Point(100, 100), 0.4);
            unpinned.OnFrame(TimeSpan.FromSeconds(1));
            Assert.Equal(0, raises); // unpinned stays below other applications
            ChaosSkiaFxOverlay.CloseActive();

            settings.ChaosPinOnTop = true;
            video = false;
            ChaosSkiaFxOverlay.TrailDot(new Point(100, 100), 0.4);
            var pinned = Assert.IsType<ChaosSkiaFxOverlay>(ChaosSkiaFxOverlay.Active);
            Assert.True(pinned.Topmost);
            Assert.Equal(x11 ? 1 : 0, raises); // X11 needs a raise even without a video
            video = true;
            ChaosSkiaFxOverlay.TrailDot(new Point(100, 100), 0.4);
            Assert.Equal(x11 ? 2 : 1, raises);
            video = false;
            ChaosSkiaFxOverlay.TrailDot(new Point(100, 100), 0.4);
            Assert.Equal(x11 ? 3 : 1, raises);
            pinned.OnFrame(TimeSpan.FromSeconds(1));
            Assert.Equal(x11 ? 4 : 1, raises); // an active X11 frame keeps the field above a clicked app
            Assert.Same(pinned, ChaosSkiaFxOverlay.Active);
        }
        finally
        {
            ChaosSkiaFxOverlay.CloseActive();
            ChaosSkiaFxOverlay.Composited = composited;
            ChaosSkiaFxOverlay.SkipPlatformChecksForTests = false;
            ChaosSkiaFxOverlay.VideoPlaying = videoPlaying;
            ChaosSkiaFxOverlay.X11NeedsRaise = needsRaise;
            ChaosSkiaFxOverlay.Raise = raise;
            settings.ChaosSkiaFxEnabled = enabled;
            settings.ChaosPinOnTop = pin;
        }
        return Task.CompletedTask;
    });
    [Fact]
    public Task LessonCardStaysAboveRaisedGameplay() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var settings = CoreSettings.Current;
        var enabled = settings.ChaosSkiaFxEnabled; var pin = settings.ChaosPinOnTop;
        var composited = ChaosSkiaFxOverlay.Composited; var needsRaise = ChaosSkiaFxOverlay.X11NeedsRaise;
        var raise = ChaosSkiaFxOverlay.Raise; var raised = new System.Collections.Generic.List<global::Avalonia.Controls.Window>();
        try
        {
            settings.ChaosSkiaFxEnabled = settings.ChaosPinOnTop = true;
            ChaosSkiaFxOverlay.Composited = () => true;
            ChaosSkiaFxOverlay.SkipPlatformChecksForTests = true;
            ChaosSkiaFxOverlay.X11NeedsRaise = _ => true;
            ChaosSkiaFxOverlay.Raise = raised.Add;
            ChaosUnlockCardOverlay.Show(ConditioningControlPanel.Services.Chaos.ChaosUnlockCardRules.ForLesson("taking_chances")!, autoDismiss: false);
            ChaosSkiaFxOverlay.TrailDot(new Point(100, 100), 0.4);
            Assert.Same(ChaosUnlockCardOverlay.Active, raised[^1]);
        }
        finally
        {
            ChaosUnlockCardOverlay.CloseActive();
            ChaosSkiaFxOverlay.CloseActive(); ChaosSkiaFxOverlay.Composited = composited;
            ChaosSkiaFxOverlay.SkipPlatformChecksForTests = false;
            ChaosSkiaFxOverlay.X11NeedsRaise = needsRaise; ChaosSkiaFxOverlay.Raise = raise;
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
            ChaosRunHost.StartRun(new ConditioningControlPanel.Services.Chaos.ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
            Assert.True(ChaosRunHost.IsActive);
            ChaosSkiaFxOverlay.ArmCursorGlow();
            Assert.NotNull(attempted);
            Assert.False(attempted.IsVisible);
            Assert.Null(ChaosSkiaFxOverlay.Active);
            attempted = null;
            ChaosSkiaFxOverlay.ArmCursorGlow();
            Assert.Null(attempted); // do not retry a refused full-screen surface in this run
            ChaosRunHost.ForceShutdown();
            ChaosRunHost.StartRun(new ConditioningControlPanel.Services.Chaos.ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
            Assert.True(ChaosRunHost.IsActive);
            ChaosSkiaFxOverlay.ManagedClickThrough = _ => true;
            ChaosSkiaFxOverlay.ArmCursorGlow();
            Assert.NotNull(ChaosSkiaFxOverlay.Active); // a later run can recover
        }
        finally
        {
            ChaosRunHost.ForceShutdown();
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
