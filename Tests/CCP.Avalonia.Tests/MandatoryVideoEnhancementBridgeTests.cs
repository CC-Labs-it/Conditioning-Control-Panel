using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class MandatoryVideoEnhancementBridgeTests
{
    [Fact]
    public void SidecarBindsAndDispatchesUntilUnbound()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-video-enh-").FullName;
        var path = Path.Combine(dir, "clip.mp4");
        var old = EnhancementHostService.DispatcherFactory;
        var dispatcher = new Recorder();
        EnhancementHostService.DispatcherFactory = () => dispatcher;
        try
        {
            var enhancement = new Enhancement { MediaType = MediaTypes.Video, Metadata = new EnhancementMetadata { Name = "test", Creator = "test" } };
            enhancement.TimelineItems.Add(new TimelineItem { Kind = TimelineItemKind.Rule, Trigger = new TimeReachedTrigger { Time = 1 }, Action = new TriggerEffectAction { EffectType = EffectTypes.Flash, DurationMs = 500 }, CooldownMs = 0 });
            File.WriteAllText(Path.Combine(dir, "clip.ccpenh.json"), EnhancementSerializer.Save(enhancement));
            var source = new Source();
            using var bridge = new MandatoryVideoEnhancementBridge();
            Assert.True(bridge.Bind(path, true, source));
            Assert.True(bridge.IsBound);
            source.Advance(1.1);
            Assert.Single(dispatcher.Actions);
            bridge.Unbind();
            Assert.False(bridge.IsBound);
            source.Advance(0);
            source.Advance(1.1);
            Assert.Single(dispatcher.Actions);
        }
        finally { EnhancementHostService.DispatcherFactory = old; Directory.Delete(dir, true); }
    }

    [Fact]
    public void DisabledOrMissingSidecarDoesNotBindAndClearsPreviousBinding()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-video-enh-").FullName;
        var path = Path.Combine(dir, "clip.mp4");
        try
        {
            var enhancement = new Enhancement { MediaType = MediaTypes.Video, Metadata = new EnhancementMetadata { Name = "test", Creator = "test" } };
            File.WriteAllText(Path.Combine(dir, "clip.ccpenh.json"), EnhancementSerializer.Save(enhancement));
            using var bridge = new MandatoryVideoEnhancementBridge();
            var source = new Source();
            Assert.False(bridge.Bind(path, false, source));
            Assert.True(bridge.Bind(path, true, source));
            Assert.False(bridge.Bind(path, false, source));
            Assert.False(bridge.IsBound);
            Assert.False(bridge.Bind(Path.Combine(dir, "missing.mp4"), true, source));
            Assert.False(bridge.IsBound);
        }
        finally { Directory.Delete(dir, true); }
    }

    private sealed class Source : IPlaybackTimeSource
    {
        private double _time;
        public event Action<double>? PlaybackTimeChanged;
        public void Advance(double time) { _time = time; PlaybackTimeChanged?.Invoke(time); }
        public double GetCurrentTimeSeconds() => _time;
        public double GetDurationSeconds() => 60;
        public bool IsPlaying => true;
        public void Seek(double seconds) => _time = seconds;
        public void Pause() { }
        public void Play() { }
        public PlaybackRect GetVideoRect() => PlaybackRect.Empty;
    }
    private sealed class Recorder : IActionDispatcher
    {
        public List<EnhancementAction> Actions { get; } = new();
        public Task DispatchAsync(EnhancementAction action, EnhancementDispatchContext context, CancellationToken ct = default)
        { Actions.Add(action); return Task.CompletedTask; }
    }
}
