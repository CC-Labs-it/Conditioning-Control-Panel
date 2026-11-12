using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public class MandatoryVideoEnhancementTests
{
    [Fact]
    public void SidecarBindsOnlyWhenEnabledAndStopsOnEveryCleanup() => AvaloniaTestDispatcher.Run(() =>
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-enh-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "clip.webm");
        var e = new Enhancement { MediaType = MediaTypes.Video, Metadata = new EnhancementMetadata { Name = "test", Creator = "test" } };
        e.TimelineItems.Add(new TimelineItem { Kind = TimelineItemKind.Rule, Trigger = new TimeReachedTrigger { Time = 1 },
            Action = new TriggerEffectAction { EffectType = EffectTypes.Flash, DurationMs = 500 } });
        e.TimelineItems.Add(new TimelineItem { Kind = TimelineItemKind.Rule, Trigger = new TimeReachedTrigger { Time = 2 },
            Action = new TriggerEffectAction { EffectType = EffectTypes.Flash, DurationMs = 500 } });
        File.WriteAllText(Path.Combine(dir, "clip.ccpenh.json"), EnhancementSerializer.Save(e));
        var settings = CoreSettings.Current;
        var enabled = settings.VideoEnhanceIfPossible;
        var oldDispatcher = EnhancementHostService.DispatcherFactory;
        var oldWebcam = EnhancementHostService.WebcamProvider;
        var oldVideo = CoreEngine.Video;
        var oldPost = CoreDispatch.PostProvider;
        var oldFreeze = CoreSubliminal.BambiFreezeProvider;
        var attention = settings.AttentionChecksEnabled;
        var clock = new Clock();
        var recorder = new Counter();
        var overlay = new MandatoryVideoOverlay(() => new Decoder());
        overlay.Scheduler = new MandatoryVideoScheduler(overlay, clock, () => new[] { path });
        try
        {
            EnhancementHostService.DispatcherFactory = () => recorder;
            EnhancementHostService.WebcamProvider = null;
            CoreDispatch.PostProvider = action => action();
            CoreSubliminal.BambiFreezeProvider = null;
            settings.AttentionChecksEnabled = false;
            settings.VideoEnhanceIfPossible = false;
            overlay.Show(path, false);
            Assert.False(overlay.EnhancementRunning);
            overlay.ReportPlaybackTime(1500);
            Assert.Equal(0, recorder.Fired);
            overlay.CloseAll();

            settings.VideoEnhanceIfPossible = true;
            Assert.True(overlay.Scheduler.Trigger(strictOverride: false, path: path));
            clock.Fire(); // pre-roll completes and starts the fake decoder
            Assert.True(overlay.EnhancementRunning);
            overlay.ReportPlaybackTime(1500);
            Assert.Equal(1, recorder.Fired);
            overlay.OnNaturalVideoEnd();
            overlay.ReportPlaybackTime(2500);
            Assert.Equal(1, recorder.Fired);

            overlay.Show(path, false);
            overlay.ReportPlaybackTime(1500);
            Assert.Equal(2, recorder.Fired);
            CoreEngine.Video = overlay.Scheduler;
            MainShellWindow.StopEngine(); // also used by the panic surface
            overlay.ReportPlaybackTime(2500);
            Assert.Equal(2, recorder.Fired);

            EnhancementHostService.DispatcherFactory = null;
            overlay.Show(path, false);
            Assert.False(overlay.EnhancementRunning);
            overlay.ReportPlaybackTime(1500);
            Assert.Equal(2, recorder.Fired);
        }
        finally
        {
            overlay.CloseAll();
            CoreEngine.Video = oldVideo;
            CoreDispatch.PostProvider = oldPost;
            CoreSubliminal.BambiFreezeProvider = oldFreeze;
            settings.AttentionChecksEnabled = attention;
            EnhancementHostService.DispatcherFactory = oldDispatcher;
            EnhancementHostService.WebcamProvider = oldWebcam;
            settings.VideoEnhanceIfPossible = enabled;
            Directory.Delete(dir, true);
        }
    });

    private sealed class Counter : IActionDispatcher
    {
        internal int Fired;
        public Task DispatchAsync(EnhancementAction action, EnhancementDispatchContext ctx, CancellationToken ct = default)
        { Fired++; return Task.CompletedTask; }
    }
    private sealed class Decoder : IMandatoryPlayback
    {
        public long Time { get; set; }
        public long Length => 60000;
        public bool IsPlaying => true;
        public bool IsSeekable => true;
        public bool HasVideo => true;
        public void SetPause(bool paused) { }
        public void Stop() { }
        public void Dispose() { }
    }
    private sealed class Clock : TimeProvider
    {
        private ManualTimer? _timer;
        public void Fire() => _timer!.Fire();
        public override ITimer CreateTimer(TimerCallback cb, object? state, TimeSpan due, TimeSpan period)
            => _timer = new ManualTimer(() => cb(state));
        private sealed class ManualTimer(Action fire) : ITimer
        {
            public void Fire() => fire();
            public bool Change(TimeSpan due, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => default;
        }
    }

}
