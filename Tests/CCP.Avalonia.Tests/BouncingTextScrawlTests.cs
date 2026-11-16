using System;
using System.Threading.Tasks;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Super;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class BouncingTextScrawlTests
{
    [Fact]
    public async Task Panic_stops_standalone_bouncing_text_even_without_an_engine()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current == null)
                ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var saved = (BouncingTextOverlay.SkipPlatformChecksForTests, BouncingTextOverlay.ManualFramesForTests);
            var host = new Window();
            try
            {
                host.Show();
                BouncingTextOverlay.SkipPlatformChecksForTests = true;
                BouncingTextOverlay.ManualFramesForTests = true;
                BouncingTextOverlay.Start(host);
                Assert.True(BouncingTextOverlay.IsRunning);
                Assert.Single(PanicSurfaces.All, s => s.Id == "bouncing-text").Stop(null);
                Assert.False(BouncingTextOverlay.IsRunning);
            }
            finally
            {
                BouncingTextOverlay.Stop();
                host.Close();
                (BouncingTextOverlay.SkipPlatformChecksForTests, BouncingTextOverlay.ManualFramesForTests) = saved;
            }
            return Task.CompletedTask;
        });
    }
    [Fact]
    public async Task Scrawl_switch_bounces_slams_and_clears_on_stop()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current == null)
                ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var settings = CoreSettings.Current;
            var old = (BouncingTextOverlay.SkipPlatformChecksForTests, BouncingTextOverlay.ManualFramesForTests,
                BouncingTextOverlay.ScrawlOn, BouncingTextOverlay.ClickOverOwnWindow, BouncingTextOverlay.RaiseAll,
                settings.ScrawlStampCap, settings.MotionLevel, settings.ScrawlSlam);
            bool on = false;
            var host = new Window();
            try
            {
                host.Show();
                BouncingTextOverlay.SkipPlatformChecksForTests = true;
                BouncingTextOverlay.ManualFramesForTests = true;
                BouncingTextOverlay.ScrawlOn = () => on;
                BouncingTextOverlay.ClickOverOwnWindow = (_, _) => false;
                BouncingTextOverlay.RaiseAll = () => { };
                settings.ScrawlStampCap = 6; settings.MotionLevel = MotionLevel.Full; settings.ScrawlSlam = 100;
                BouncingTextOverlay.Start(host);
                var engine = BouncingTextOverlay.Engine;
                var l = engine.Logos[0];
                l.PosX = engine.MinX - 1; l.VelX = -100;
                BouncingTextOverlay.Advance(0.016);
                Assert.Null(BouncingTextOverlay.Scrawl);
                on = true; SuperAccess.RaiseChanged(SuperEffect.Scrawl);
                var scene = Assert.IsType<ScrawlScene>(BouncingTextOverlay.Scrawl);
                for (int i = 0; i < 10; i++)
                {
                    l.PosX = engine.MinX - 1; l.PosY = (engine.MinY + engine.MaxY) / 2; l.VelX = -100; l.VelY = 0;
                    BouncingTextOverlay.Advance(0.016);
                }
                Assert.Equal(6, scene.Stamps.Count);
                scene.Tick(60, settings.ScrawlStampSeconds, settings.ScrawlStampCap);
                Assert.Empty(scene.Stamps);
                l.PosX = engine.MinX + 100; l.PosY = engine.MinY + 100;
                BouncingTextOverlay.ClickOverOwnWindow = (_, _) => true;
                BouncingTextOverlay.ScrawlClick((int)(l.PosX + 1), (int)(l.PosY + 1));
                Assert.Equal(-1, l.SlamT);
                BouncingTextOverlay.ClickOverOwnWindow = (_, _) => false;
                settings.ScrawlSlam = 0;
                BouncingTextOverlay.ScrawlClick((int)(l.PosX + 1), (int)(l.PosY + 1));
                Assert.Equal(-1, l.SlamT);
                settings.ScrawlSlam = 100;
                BouncingTextOverlay.ScrawlClick((int)(l.PosX + 1), (int)(l.PosY + 1));
                Assert.Equal(0, l.SlamT);
                for (int i = 0; i < 3; i++) BouncingTextOverlay.Advance(0.1);
                Assert.Equal(l.SlamX, l.PosX + l.TextWidth / 2, 3);
                Assert.Equal(l.SlamY, l.PosY + l.TextHeight / 2, 3);
                Assert.Contains(scene.Stamps, stamp => stamp.Slam);
                on = false; SuperAccess.RaiseChanged(SuperEffect.Scrawl);
                Assert.Null(BouncingTextOverlay.Scrawl); Assert.Empty(scene.Stamps); Assert.Equal(-1, l.SlamT);
                on = true; SuperAccess.RaiseChanged(SuperEffect.Scrawl);
                scene = Assert.IsType<ScrawlScene>(BouncingTextOverlay.Scrawl);
                on = false; BouncingTextOverlay.Advance(0.016); // tier event may be unavailable
                Assert.Null(BouncingTextOverlay.Scrawl);
                on = true; SuperAccess.RaiseChanged(SuperEffect.Scrawl);
                scene = Assert.IsType<ScrawlScene>(BouncingTextOverlay.Scrawl);
                l.PosX = engine.MinX - 1; l.VelX = -100; BouncingTextOverlay.Advance(0.016);
                Assert.NotEmpty(scene.Stamps);
                BouncingTextOverlay.Stop();
                Assert.Null(BouncingTextOverlay.Scrawl); Assert.Empty(scene.Stamps); Assert.False(BouncingTextOverlay.IsRunning);
                BouncingTextOverlay.Start(host);
                scene = Assert.IsType<ScrawlScene>(BouncingTextOverlay.Scrawl);
                l = engine.Logos[0]; l.PosX = engine.MinX - 1; l.VelX = -100;
                BouncingTextOverlay.Advance(0.016); Assert.NotEmpty(scene.Stamps);
                PanicSurfaces.All.Single(s => s.Id == "bouncing-text").Stop(null);
                Assert.Null(BouncingTextOverlay.Scrawl); Assert.Empty(scene.Stamps); Assert.False(BouncingTextOverlay.IsRunning);
            }
            finally
            {
                BouncingTextOverlay.Stop(); host.Close();
                (BouncingTextOverlay.SkipPlatformChecksForTests, BouncingTextOverlay.ManualFramesForTests,
                    BouncingTextOverlay.ScrawlOn, BouncingTextOverlay.ClickOverOwnWindow, BouncingTextOverlay.RaiseAll,
                    settings.ScrawlStampCap, settings.MotionLevel, settings.ScrawlSlam) = old;
            }
            return Task.CompletedTask;
        });
    }
    private sealed class BlockingListener : IDisposable
    {
        public readonly ManualResetEventSlim Entered = new(false), Release = new(false);
        public void Dispose() { Entered.Set(); Release.Wait(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task Stop_closes_before_listener_disposal_and_drops_late_clicks()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current == null)
                ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var saved = (BouncingTextOverlay.SkipPlatformChecksForTests, BouncingTextOverlay.ManualFramesForTests,
                BouncingTextOverlay.ScrawlListenWhenTesting, BouncingTextOverlay.ScrawlOn, BouncingTextOverlay.ScrawlListen);
            var host = new Window(); var listener = new BlockingListener(); Action<int, int>? click = null;
            try
            {
                host.Show(); BouncingTextOverlay.SkipPlatformChecksForTests = true; BouncingTextOverlay.ManualFramesForTests = true;
                BouncingTextOverlay.ScrawlListenWhenTesting = true; BouncingTextOverlay.ScrawlOn = () => true;
                BouncingTextOverlay.ScrawlListen = callback => { click = callback; return listener; };
                BouncingTextOverlay.Start(host);
                Assert.NotNull(BouncingTextOverlay.Scrawl); Assert.NotNull(click);
                var watch = Stopwatch.StartNew();
                BouncingTextOverlay.Stop();
                Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2));
                Assert.False(BouncingTextOverlay.IsRunning); Assert.Null(BouncingTextOverlay.Scrawl);
                Assert.True(listener.Entered.Wait(TimeSpan.FromSeconds(2)));
                click!(1, 1); Dispatcher.UIThread.RunJobs();
                Assert.Null(BouncingTextOverlay.Scrawl);
            }
            finally
            {
                listener.Release.Set(); BouncingTextOverlay.Stop(); host.Close();
                (BouncingTextOverlay.SkipPlatformChecksForTests, BouncingTextOverlay.ManualFramesForTests,
                    BouncingTextOverlay.ScrawlListenWhenTesting, BouncingTextOverlay.ScrawlOn, BouncingTextOverlay.ScrawlListen) = saved;
            }
            return Task.CompletedTask;
        });
    }
}
