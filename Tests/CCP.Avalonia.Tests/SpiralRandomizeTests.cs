using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class SpiralRandomizeTests
{
    [Fact]
    public async Task Decode_and_opacity_refresh_keep_the_random_pick_until_stop()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-spiral-randomize-");
        var gif = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");
        var first = Path.Combine(dir.FullName, "first.gif");
        var second = Path.Combine(dir.FullName, "second.gif");
        File.WriteAllBytes(first, gif);
        File.WriteAllBytes(second, gif);
        try
        {
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                if (Application.Current is null)
                    AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                        .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                        .SetupWithoutStarting();
                var s = CoreSettings.Current;
                var saved = (s.SpiralPath, s.SpiralRandomize, s.SpiralEnabled, s.SpiralOpacity);
                var provider = CoreSession.IsEngineRunningProvider;
                var host = new Window();
                host.Show();
                try
                {
                    while (SpiralOverlay.Decoding is { IsCompleted: false } pending) await pending;
                    SpiralOverlay.CloseAll();
                    SpiralOverlay.SkipPlatformChecksForTests = true;
                    CoreSession.IsEngineRunningProvider = () => true;
                    (s.SpiralPath, s.SpiralRandomize, s.SpiralEnabled) = (first, true, true);
                    SpiralOverlay.Refresh(host);
                    while (SpiralOverlay.Decoding is { IsCompleted: false } pending) await pending;
                    Assert.True(SpiralOverlay.IsShowing);
                    var pick = SpiralOverlay.SourcePath();
                    var window = SpiralOverlay.Shown[0];
                    s.SpiralOpacity = 80;
                    SpiralOverlay.Refresh(host);
                    Assert.Same(window, SpiralOverlay.Shown[0]);
                    Assert.Equal(pick, SpiralOverlay.SourcePath());
                    Assert.Equal(SpiralFrames.Paint(0.8), window.Spiral.Opacity, 10);
                    SpiralOverlay.CloseAll();
                    Assert.False(SpiralOverlay.IsShowing);
                    SpiralOverlay.Refresh(host);
                    while (SpiralOverlay.Decoding is { IsCompleted: false } pending) await pending;
                    Assert.True(SpiralOverlay.IsShowing);
                    Assert.NotEqual(pick, SpiralOverlay.SourcePath());
                }
                finally
                {
                    SpiralOverlay.CloseAll();
                    SpiralOverlay.SkipPlatformChecksForTests = false;
                    CoreSession.IsEngineRunningProvider = provider;
                    (s.SpiralPath, s.SpiralRandomize, s.SpiralEnabled, s.SpiralOpacity) = saved;
                    host.Close();
                }
            });
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public async Task An_undecodable_random_pick_is_repicked_instead_of_leaving_the_run_blank()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-spiral-randomize-");
        var good = Path.Combine(dir.FullName, "good.gif");
        var broken = Path.Combine(dir.FullName, "broken.gif");
        File.WriteAllBytes(good, Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7"));
        File.WriteAllText(broken, "not a gif");
        try
        {
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                if (Application.Current is null)
                    AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                        .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                        .SetupWithoutStarting();
                var s = CoreSettings.Current;
                var saved = (s.SpiralPath, s.SpiralRandomize, s.SpiralEnabled);
                var provider = CoreSession.IsEngineRunningProvider;
                var host = new Window();
                host.Show();
                try
                {
                    while (SpiralOverlay.Decoding is { IsCompleted: false } pending) await pending;
                    SpiralOverlay.SkipPlatformChecksForTests = true;
                    CoreSession.IsEngineRunningProvider = () => true;
                    (s.SpiralPath, s.SpiralRandomize, s.SpiralEnabled) = (good, true, true);
                    // The pool avoids the previous pick, so once good has shown every later run picks the broken file first.
                    for (var run = 0; run < 4; run++)
                    {
                        SpiralOverlay.CloseAll();
                        SpiralOverlay.Refresh(host);
                        while (SpiralOverlay.Decoding is { IsCompleted: false } pending) await pending;
                        Assert.True(SpiralOverlay.IsShowing, $"run {run} left the spiral blank");
                        Assert.Equal(good, SpiralOverlay.SourcePath());
                    }
                }
                finally
                {
                    SpiralOverlay.CloseAll();
                    SpiralOverlay.SkipPlatformChecksForTests = false;
                    CoreSession.IsEngineRunningProvider = provider;
                    (s.SpiralPath, s.SpiralRandomize, s.SpiralEnabled) = saved;
                    host.Close();
                }
            });
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public async Task Randomize_picks_once_per_run_and_the_next_run_avoids_the_previous_image()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-spiral-randomize-");
        var first = Path.Combine(dir.FullName, "first.gif");
        var second = Path.Combine(dir.FullName, "second.png");
        File.WriteAllText(first, "pool entry");
        File.WriteAllText(second, "pool entry");
        try
        {
            await AvaloniaTestDispatcher.RunAsync(() =>
            {
                var s = CoreSettings.Current;
                var saved = (s.SpiralPath, s.SpiralRandomize);
                try
                {
                    SpiralOverlay.CloseAll();
                    (s.SpiralPath, s.SpiralRandomize) = (first, true);
                    var pick = SpiralOverlay.SourcePath();
                    Assert.Contains(pick, new[] { first, second });
                    for (var i = 0; i < 10; i++) Assert.Equal(pick, SpiralOverlay.SourcePath());
                    // Toggling during a run only changes the next start, matching the card's promise.
                    s.SpiralRandomize = false;
                    Assert.Equal(pick, SpiralOverlay.SourcePath());
                    s.SpiralRandomize = true;
                    SpiralOverlay.CloseAll();
                    Assert.NotEqual(pick, SpiralOverlay.SourcePath());
                    SpiralOverlay.CloseAll();
                    s.SpiralRandomize = false;
                    Assert.Equal(first, SpiralOverlay.SourcePath());
                    s.SpiralRandomize = true;
                    Assert.Equal(first, SpiralOverlay.SourcePath());
                    s.SpiralPath = second;
                    Assert.Equal(second, SpiralOverlay.SourcePath());
                }
                finally
                {
                    SpiralOverlay.CloseAll();
                    (s.SpiralPath, s.SpiralRandomize) = saved;
                }
                return Task.CompletedTask;
            });
        }
        finally { dir.Delete(true); }
    }
}
