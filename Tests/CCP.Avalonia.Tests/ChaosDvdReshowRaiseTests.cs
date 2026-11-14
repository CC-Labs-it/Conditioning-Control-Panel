using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Xunit;

namespace CCP.Avalonia.Tests;

public class ChaosDvdReshowRaiseTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ReusedLogoRaisesImmediatelyOnlyWhenPinnedAndPlaying(bool playingVideo, bool pin)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var settings = CoreSettings.Current;
            var (oldPin, oldShared) = (settings.ChaosPinOnTop, settings.ChaosDvdSharedHost);
            var (can, prep, sfx, spanker) = (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.PlaySfx, ChaosDvdOverlay.SpankerRedirect);
            var oldRaise = ChaosRunHost.RaiseOverlayWindow;
            var oldVideo = CoreEngine.Video;
            var scheduler = MandatoryVideoOverlay.Instance.Scheduler;
            var playing = scheduler.GetType().GetField("_playing", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var oldPlaying = playing.GetValue(scheduler);
            var raised = new List<Window>();
            var host = new Window();
            host.Show();
            try
            {
                settings.ChaosDvdSharedHost = false;
                settings.ChaosPinOnTop = pin;
                ChaosDvdOverlay.CanOverlay = () => true;
                ChaosDvdOverlay.PrepareWindow = (_, _) => true;
                ChaosDvdOverlay.PlaySfx = (_, _) => { };
                ChaosDvdOverlay.SpankerRedirect = () => false;
                ChaosRunHost.RaiseOverlayWindow = w => { Assert.True(w.IsVisible); raised.Add(w); };
                CoreEngine.Video = scheduler;
                playing.SetValue(scheduler, false);
                ChaosDvdOverlay.Launch(host, 1, 1, 1);
                var window = Assert.IsType<Window>(ChaosDvdOverlay.Window);
                ChaosDvdOverlay.Advance(2);
                ChaosDvdOverlay.Advance(1);
                Assert.False(window.IsVisible);
                playing.SetValue(scheduler, playingVideo);
                raised.Clear();
                ChaosDvdOverlay.Launch(host, 1, 1, 1);
                Assert.Same(window, ChaosDvdOverlay.Window);
                Assert.True(window.IsVisible);
                if (playingVideo && pin) Assert.Equal(new[] { window }, raised);
                else Assert.Empty(raised);
            }
            finally
            {
                ChaosDvdOverlay.CloseActive();
                host.Close();
                playing.SetValue(scheduler, oldPlaying);
                CoreEngine.Video = oldVideo;
                ChaosRunHost.RaiseOverlayWindow = oldRaise;
                (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.PlaySfx, ChaosDvdOverlay.SpankerRedirect) = (can, prep, sfx, spanker);
                (settings.ChaosPinOnTop, settings.ChaosDvdSharedHost) = (oldPin, oldShared);
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task SharedHostIsRaisedOnCreationDuringVideo()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var settings = CoreSettings.Current;
            var (pin, shared) = (settings.ChaosPinOnTop, settings.ChaosDvdSharedHost);
            var (can, prep, sfx) = (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.PlaySfx);
            var oldRaise = ChaosRunHost.RaiseOverlayWindow;
            var oldVideo = CoreEngine.Video;
            var scheduler = MandatoryVideoOverlay.Instance.Scheduler;
            var playing = scheduler.GetType().GetField("_playing", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var oldPlaying = playing.GetValue(scheduler);
            var raised = new List<Window>();
            var host = new Window(); host.Show();
            try
            {
                settings.ChaosPinOnTop = true;
                settings.ChaosDvdSharedHost = true;
                ChaosDvdOverlay.CanOverlay = () => true;
                ChaosDvdOverlay.PrepareWindow = (_, _) => true;
                ChaosDvdOverlay.PlaySfx = (_, _) => { };
                ChaosRunHost.RaiseOverlayWindow = w => { Assert.True(w.IsVisible); raised.Add(w); };
                CoreEngine.Video = scheduler;
                playing.SetValue(scheduler, true);
                ChaosDvdOverlay.Launch(host, 1, 1, 1);
                Assert.Equal(new[] { ChaosDvdOverlay.Window }, raised);
            }
            finally
            {
                ChaosDvdOverlay.CloseActive(); host.Close();
                playing.SetValue(scheduler, oldPlaying); CoreEngine.Video = oldVideo;
                ChaosRunHost.RaiseOverlayWindow = oldRaise;
                (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.PlaySfx) = (can, prep, sfx);
                (settings.ChaosPinOnTop, settings.ChaosDvdSharedHost) = (pin, shared);
            }
            return Task.CompletedTask;
        });
    }
}
