using System;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosHeavyQuarantineTests
{
    private sealed class Host : IMandatoryVideoHost
    {
        public void Show(string path, bool strict) { }
        public double CloseAll() => 0;
        public void ShowMessage(AttentionVerdict verdict, int ms, Action then) { }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Video_teardown_gates_rain_and_unsubscribes_on_stop_or_panic(bool panic)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var previousVideo = CoreEngine.Video;
            var previousClock = BubbleOverlay.Field.Clock;
            var previousFirer = EffectPayload.Firer;
            var now = DateTime.UtcNow;
            var video = new MandatoryVideoScheduler(new Host(), library: () => new[] { "/v/a.mp4" });
            int fired = 0;
            BubbleOverlay.Field.Clock = () => now;
            EffectPayload.Firer = _ => fired++;
            CoreEngine.Video = video;
            void EndVideo() { Assert.True(video.Trigger(true)); video.ForceCleanup(); }
            void Rain() => ChaosRunHost.FirePayloadForDetonation(new EffectBubbleSpec { Payload = new GifCascadePayload() });
            try
            {
                ChaosRunHost.ForceShutdown();
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
                ChaosRunHost.BeginRun();
                EndVideo();
                now = now.AddSeconds(2); Rain(); Assert.Equal(0, fired);
                now = now.AddSeconds(1.1); Rain(); Assert.Equal(1, fired);
                EndVideo();
                now = now.AddSeconds(3.1); Rain(); Assert.Equal(1, fired);
                now = now.AddSeconds(8); Rain(); Assert.Equal(2, fired);
                if (panic) ChaosRunHost.ForceShutdown(); else ChaosRunHost.RequestStop();
                now = now.AddSeconds(12);
                EndVideo(); Rain(); Assert.Equal(3, fired);
            }
            finally
            {
                ChaosRunHost.ForceShutdown(); video.ForceCleanup();
                CoreEngine.Video = previousVideo; BubbleOverlay.Field.Clock = previousClock; EffectPayload.Firer = previousFirer;
            }
            return Task.CompletedTask;
        });
    }
}
