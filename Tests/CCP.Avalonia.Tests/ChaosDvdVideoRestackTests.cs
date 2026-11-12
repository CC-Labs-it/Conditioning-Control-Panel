using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public class ChaosDvdVideoRestackTests
{
    [Fact]
    public async Task VideoEventsAndFourthPlayingTickRaiseEveryVisibleLogo()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var settings = CoreSettings.Current;
            var pin = settings.ChaosPinOnTop;
            var shared = settings.ChaosDvdSharedHost;
            var (can, prep, frames, sfx) = (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.FrameSourceFor, ChaosDvdOverlay.PlaySfx);
            var oldRaise = ChaosRunHost.RaiseOverlayWindow;
            var oldVideo = CoreEngine.Video;
            var scheduler = MandatoryVideoOverlay.Instance.Scheduler;
            var playing = scheduler.GetType().GetField("_playing", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var oldPlaying = playing.GetValue(scheduler);
            var raised = new List<Window>();
            Window? video = null;
            try
            {
                settings.ChaosPinOnTop = true;
                settings.ChaosDvdSharedHost = false;
                ChaosDvdOverlay.CanOverlay = () => true;
                ChaosDvdOverlay.PrepareWindow = (_, _) => true;
                ChaosDvdOverlay.PlaySfx = (_, _) => { };
                ChaosRunHost.RaiseOverlayWindow = raised.Add;
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
                ChaosRunHost.Overlay!.FinishCountdown();
                ChaosRunHost.StopTimerForTests();
                ChaosDvdOverlay.Launch(ChaosRunHost.Overlay, 30, 1, 1, count: 2);
                var logos = ChaosDvdOverlay.Windows.Where(w => w.IsVisible).ToArray();
                Assert.Equal(2, logos.Length);
                Assert.True(ChaosRunHost.IsDescending);
                video = MandatoryVideoOverlay.Instance.Build(null, false).Window;
                video.WindowState = WindowState.Normal;
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(120);
                Assert.All(logos, w => Assert.Contains(w, raised));
                raised.Clear();
                var pointer = new global::Avalonia.Input.Pointer(global::Avalonia.Input.Pointer.GetNextFreeId(), PointerType.Mouse, true);
                var click = new PointerPressedEventArgs(video, pointer, video, new Point(10, 10), 0,
                    new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None) { Handled = true };
                video.RaiseEvent(click);
                await Task.Delay(120);
                Assert.All(logos, w => Assert.Contains(w, raised));
                raised.Clear();
                settings.ChaosPinOnTop = false;
                video.RaiseEvent(click);
                await Task.Delay(120);
                Assert.Empty(raised);
                settings.ChaosPinOnTop = true;
                CoreEngine.Video = scheduler;
                playing.SetValue(scheduler, false);
                for (int i = 0; i < 4; i++) ChaosRunHost.Tick();
                Assert.DoesNotContain(raised, logos.Contains);
                playing.SetValue(scheduler, true);
                raised.Clear();
                for (int i = 0; i < 3; i++) ChaosRunHost.Tick();
                Assert.DoesNotContain(raised, logos.Contains);
                ChaosRunHost.Tick();
                Assert.All(logos, w => Assert.Contains(w, raised));
                raised.Clear();
                video.RaiseEvent(click);
                video.Close();
                raised.Clear();
                await Task.Delay(120);
                Assert.Empty(raised);
                ChaosRunHost.ForceShutdown();
                raised.Clear();
                ChaosRunHost.RaiseDvdAboveVideo();
                Assert.Empty(raised);
            }
            finally
            {
                playing.SetValue(scheduler, oldPlaying);
                CoreEngine.Video = oldVideo;
                video?.Close();
                ChaosRunHost.ForceShutdown();
                ChaosDvdOverlay.CloseActive();
                (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.FrameSourceFor, ChaosDvdOverlay.PlaySfx) = (can, prep, frames, sfx);
                ChaosRunHost.RaiseOverlayWindow = oldRaise;
                settings.ChaosPinOnTop = pin;
                settings.ChaosDvdSharedHost = shared;
            }
        });
    }
    [Fact]
    public async Task VideoEventsAndPlayingTicksKeepDvdBelowClockChromeAndBubbles()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var settings = CoreSettings.Current;
            var (pin, shared) = (settings.ChaosPinOnTop, settings.ChaosDvdSharedHost);
            var (can, prep, sfx) = (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.PlaySfx);
            var (overlayRaise, chromeRaise, bubbleRaise) = (ChaosRunHost.RaiseOverlayWindow, ChaosRunHost.RaiseChromeWindow, ChaosRunHost.RaiseBubbles);
            var (watchRaise, skipWatch) = (ChaosWaveTimerWindow.RaiseWindow, ChaosWaveTimerWindow.SkipPlatformChecksForTests);
            var oldVideo = CoreEngine.Video;
            var scheduler = MandatoryVideoOverlay.Instance.Scheduler;
            var playing = scheduler.GetType().GetField("_playing", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var oldPlaying = playing.GetValue(scheduler);
            var order = new List<string>();
            Window? video = null;
            try
            {
                settings.ChaosPinOnTop = true;
                settings.ChaosDvdSharedHost = false;
                ChaosDvdOverlay.CanOverlay = () => true;
                ChaosDvdOverlay.PrepareWindow = (_, _) => true;
                ChaosDvdOverlay.PlaySfx = (_, _) => { };
                ChaosWaveTimerWindow.SkipPlatformChecksForTests = true;
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
                ChaosRunHost.State!.ShowWaveTimer = true;
                ChaosRunHost.Overlay!.FinishCountdown();
                ChaosRunHost.StopTimerForTests();
                ChaosRunHost.Tick(); // Give the wave timer a measured readout.
                ChaosDvdOverlay.Launch(ChaosRunHost.Overlay, 30, 1, 1, count: 2);
                var logos = ChaosDvdOverlay.Windows.Where(w => w.IsVisible).ToArray();
                Assert.Equal(2, logos.Length);
                Assert.NotNull(ChaosWaveTimerWindow.Active);
                ChaosRunHost.RaiseOverlayWindow = w => { if (logos.Contains(w)) order.Add("dvd"); };
                ChaosWaveTimerWindow.RaiseWindow = _ => order.Add("watch");
                ChaosRunHost.RaiseChromeWindow = _ => order.Add("chrome");
                ChaosRunHost.RaiseBubbles = () => order.Add("bubbles");

                void AssertVideoOrder()
                {
                    Assert.Contains("watch", order);
                    Assert.Contains("bubbles", order);
                    int start = 0;
                    for (int i = 0; i < order.Count; i++)
                        if (order[i] == "bubbles")
                        {
                            var kick = order.Skip(start).Take(i - start).ToArray();
                            Assert.Equal(new[] { "dvd", "dvd" }, kick.Take(2));
                            Assert.True(Array.IndexOf(kick, "chrome") >= 2);
                            Assert.True(Array.IndexOf(kick, "watch") >= 2);
                            start = i + 1;
                        }
                    Assert.Equal(order.Count, start);
                }

                video = MandatoryVideoOverlay.Instance.Build(null, false).Window;
                video.WindowState = WindowState.Normal;
                video.Width = 800;
                video.Height = 600;
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(120);
                AssertVideoOrder();
                order.Clear();
                var pointer = new global::Avalonia.Input.Pointer(global::Avalonia.Input.Pointer.GetNextFreeId(), PointerType.Mouse, true);
                video.RaiseEvent(new PointerPressedEventArgs(video, pointer, video, new Point(10, 10), 0,
                    new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None) { Handled = true });
                await Task.Delay(120);
                AssertVideoOrder();

                CoreEngine.Video = scheduler;
                playing.SetValue(scheduler, true);
                order.Clear();
                for (int i = 0; i < 3; i++) ChaosRunHost.Tick();
                Assert.Equal(new[] { "dvd", "dvd" }, order.Take(2));
                Assert.True(order.IndexOf("chrome") >= 2);
                Assert.Equal(1, order.Count(x => x == "watch"));
                Assert.True(order.IndexOf("watch") >= 2);
                Assert.True(order.IndexOf("bubbles") > order.IndexOf("watch"));
                Assert.True(order.IndexOf("bubbles") > order.IndexOf("chrome"));
            }
            finally
            {
                playing.SetValue(scheduler, oldPlaying);
                CoreEngine.Video = oldVideo;
                video?.Close();
                ChaosRunHost.ForceShutdown();
                ChaosDvdOverlay.CloseActive();
                (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.PlaySfx) = (can, prep, sfx);
                (ChaosRunHost.RaiseOverlayWindow, ChaosRunHost.RaiseChromeWindow, ChaosRunHost.RaiseBubbles) = (overlayRaise, chromeRaise, bubbleRaise);
                (ChaosWaveTimerWindow.RaiseWindow, ChaosWaveTimerWindow.SkipPlatformChecksForTests) = (watchRaise, skipWatch);
                (settings.ChaosPinOnTop, settings.ChaosDvdSharedHost) = (pin, shared);
            }
        });
    }

}
