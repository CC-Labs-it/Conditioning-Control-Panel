using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public class MandatoryVideoMinLoopTests
{
    [Fact]
    public Task Panic_closes_mandatory_video_without_waiting_for_engine_stop() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var overlay = MandatoryVideoOverlay.Instance;
        var previous = overlay.Scheduler;
        var scheduler = new MandatoryVideoScheduler(overlay, library: () => Array.Empty<string>());
        overlay.Scheduler = scheduler;
        try
        {
            Assert.True(scheduler.Trigger(false, "/test/video.mp4"));
            var surface = overlay.Build(null, false);
            ((List<MandatoryVideoOverlay.Surface>)overlay.Surfaces).Add(surface);
            surface.Window.Show();
            Assert.True(surface.Window.IsVisible);
            PanicSurfaces.All.Single(x => x.Id == "mandatory-video").Stop(null);
            Assert.False(scheduler.IsPlaying);
            Assert.Empty(overlay.Windows);
            Assert.False(surface.Window.IsVisible);
        }
        finally
        {
            scheduler.ForceCleanup();
            overlay.Scheduler = previous;
        }
        return Task.CompletedTask;
    });
}
