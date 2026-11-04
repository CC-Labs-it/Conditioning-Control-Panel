using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using System.IO;
using ConditioningControlPanel.Avalonia.Platform;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosVideoRestackTests
{
    [Fact]
    public async Task Video_start_settle_and_click_restack_only_a_pinned_run_in_order()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var settings = CoreSettings.Current;
            var pin = settings.ChaosPinOnTop;
            var oldRaise = ChaosRunHost.RaiseGameWindow;
            var oldSchedule = MandatoryVideoOverlay.ScheduleGameRestack;
            var raises = new List<Window>();
            var scheduled = new List<(Action Action, TimeSpan Delay)>();
            try
            {
                settings.ChaosPinOnTop = true;
                ChaosRunHost.RaiseGameWindow = raises.Add;
                MandatoryVideoOverlay.ScheduleGameRestack = (a, d) => scheduled.Add((a, d));
                ChaosFieldFxOverlay.SkipPlatformChecksForTests = true;
                ChaosFieldFxOverlay.Ripple(new Point(10, 10), 20, 500);
                MandatoryVideoOverlay.RestackOnVideoStart();
                Assert.Empty(raises); // even a visible field is not raised outside a run
                scheduled.Clear();
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
                ChaosRunHost.Overlay!.FinishCountdown();
                ChaosFieldFxOverlay.Ripple(new Point(10, 10), 20, 500);
                var field = ChaosFieldFxOverlay.Active!;
                var hud = ChaosRunHost.Hud!;
                var boon = ChaosRunHost.BoonBar!;
                MandatoryVideoOverlay.RestackOnVideoStart();
                Assert.Contains(field, raises);
                Assert.True(raises.IndexOf(field) < raises.IndexOf(boon));
                Assert.True(raises.IndexOf(boon) < raises.IndexOf(hud));
                var first = raises.ToArray();
                Assert.Equal(TimeSpan.FromMilliseconds(60), scheduled[0].Delay);
                raises.Clear(); scheduled[0].Action();
                Assert.Equal(first, raises);
                scheduled.Clear(); raises.Clear();
                MandatoryVideoOverlay.RestackOnVideoClick();
                Assert.Empty(raises);
                Assert.Equal(TimeSpan.FromMilliseconds(50), scheduled[0].Delay);
                scheduled[0].Action();
                Assert.Equal(first, raises);
                raises.Clear(); settings.ChaosPinOnTop = false;
                scheduled[0].Action();
                MandatoryVideoOverlay.RestackOnVideoStart();
                Assert.Empty(raises);
                settings.ChaosPinOnTop = true;
                ChaosRunHost.ForceShutdown();
                scheduled[0].Action();
                Assert.Empty(raises);
            }
            finally
            {
                ChaosRunHost.ForceShutdown();
                ChaosFieldFxOverlay.SkipPlatformChecksForTests = false;
                settings.ChaosPinOnTop = pin;
                ChaosRunHost.RaiseGameWindow = oldRaise;
                MandatoryVideoOverlay.ScheduleGameRestack = oldSchedule;
            }
            return Task.CompletedTask;
        });
    }
    [Fact]
    public async Task Video_show_and_pointer_press_raise_attention_after_gameplay()
    {
        try { _ = new LibVlcAudio("--aout=dummy"); }
        catch (Exception e) { Assert.Skip("libvlc not available: " + e.Message); }
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var settings = CoreSettings.Current;
            var pin = settings.ChaosPinOnTop;
            var oldRaise = ChaosRunHost.RaiseGameWindow;
            var oldSchedule = MandatoryVideoOverlay.ScheduleGameRestack;
            var oldPrepare = MandatoryVideoOverlay.PrepareAttentionWindow;
            var oldRegion = MandatoryVideoOverlay.SetAttentionRegion;
            var oldAttentionRaise = MandatoryVideoOverlay.RaiseAttentionWindow;
            var raises = new List<Window>();
            var scheduled = new List<(Action Action, TimeSpan Delay)>();
            var regions = new List<PixelRect[]>();
            var o = MandatoryVideoOverlay.Instance;
            try
            {
                settings.ChaosPinOnTop = true;
                ChaosRunHost.RaiseGameWindow = raises.Add;
                MandatoryVideoOverlay.RaiseAttentionWindow = raises.Add;
                MandatoryVideoOverlay.ScheduleGameRestack = (a, d) => scheduled.Add((a, d));
                MandatoryVideoOverlay.PrepareAttentionWindow = (_, _) => true;
                MandatoryVideoOverlay.SetAttentionRegion = (_, rects) => { regions.Add(rects); return true; };
                ChaosFieldFxOverlay.SkipPlatformChecksForTests = true;
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
                ChaosRunHost.Overlay!.FinishCountdown();
                ChaosFieldFxOverlay.Ripple(new Point(10, 10), 20, 500);
                o.Show(Path.Combine(AppContext.BaseDirectory, "Assets", "bubblecount-2s.mp4"), false);
                Assert.Contains(ChaosFieldFxOverlay.Active!, raises);
                Assert.Contains(scheduled, a => a.Delay == TimeSpan.FromMilliseconds(60));
                var video = Assert.Single(o.Windows);
                o.Spawn(0);
                var surface = Assert.Single(o.Surfaces);
                var attention = Assert.IsType<Window>(surface.AttentionWindow);
                Assert.Same(surface.Layer, attention.Content);
                Assert.False(attention.ShowActivated);
                var region = Assert.Single(regions[^1]);
                var firstTarget = Assert.IsType<Border>(Assert.Single(surface.Layer.Children));
                Assert.True(region.Right >= Math.Ceiling(Canvas.GetLeft(firstTarget) + firstTarget.Width));
                Assert.True(region.Bottom >= Math.Ceiling(Canvas.GetTop(firstTarget) + firstTarget.Height));
                Assert.Equal(attention, raises[^1]);
                var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                var paused = typeof(MandatoryVideoOverlay).GetField("_gracePaused", flags)!;
                var sync = typeof(MandatoryVideoOverlay).GetMethod("SyncAttentionRegion", flags)!;
                paused.SetValue(o, true); sync.Invoke(o, new object[] { surface });
                Assert.False(surface.Layer.IsVisible);
                Assert.Empty(regions[^1]);
                paused.SetValue(o, false); sync.Invoke(o, new object[] { surface });
                Assert.True(surface.Layer.IsVisible);
                Assert.Single(regions[^1]);
                var settle = Assert.Single(scheduled);
                raises.Clear(); settle.Action();
                Assert.Equal(attention, raises[^1]);
                Assert.Contains(ChaosRunHost.Hud!, raises);
                raises.Clear(); scheduled.Clear();
                video.MouseDown(new Point(2, 2), MouseButton.Left);
                video.MouseUp(new Point(2, 2), MouseButton.Left);
                var click = Assert.Single(scheduled);
                Assert.Equal(TimeSpan.FromMilliseconds(50), click.Delay);
                click.Action();
                Assert.Contains(ChaosRunHost.Hud!, raises);
                Assert.Equal(attention, raises[^1]);
                Assert.True(raises.IndexOf(ChaosRunHost.Hud!) < raises.IndexOf(attention));
                global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var target = Assert.IsType<Border>(Assert.Single(surface.Layer.Children));
                var at = new Point(Canvas.GetLeft(target) + target.Width / 2, Canvas.GetTop(target) + target.Height / 2);
                attention.MouseDown(at, MouseButton.Left);
                attention.MouseUp(at, MouseButton.Left);
                Assert.Equal(0, o.LiveTargets);
                o.Step(settings.AttentionLifespan + 1, 0);
                Assert.Empty(regions[^1]);
                MandatoryVideoOverlay.SetAttentionRegion = (_, _) => false;
                paused.SetValue(o, true);
                o.Spawn(0);
                Assert.Null(surface.AttentionWindow);
                Assert.False(attention.IsVisible);
                Assert.True(surface.Layer.IsVisible);
                Assert.Contains(surface.Layer, ((Panel)video.Content!).Children);
                paused.SetValue(o, false);
                MandatoryVideoOverlay.PrepareAttentionWindow = (_, _) => false;
                o.Spawn(0);
                Assert.Null(surface.AttentionWindow);
                Assert.Contains(surface.Layer, ((Panel)video.Content!).Children);
                o.CloseAll();
                Assert.False(attention.IsVisible);
            }
            finally
            {
                o.CloseAll(); ChaosRunHost.ForceShutdown();
                ChaosFieldFxOverlay.SkipPlatformChecksForTests = false;
                settings.ChaosPinOnTop = pin;
                ChaosRunHost.RaiseGameWindow = oldRaise;
                MandatoryVideoOverlay.ScheduleGameRestack = oldSchedule;
                MandatoryVideoOverlay.PrepareAttentionWindow = oldPrepare;
                MandatoryVideoOverlay.SetAttentionRegion = oldRegion;
                MandatoryVideoOverlay.RaiseAttentionWindow = oldAttentionRaise;
            }
            return Task.CompletedTask;
        });
    }

}
