using System;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class EmiRingCueTests
{
    [Fact]
    public void RingCueUsesTheModsFormatSwapThenFallsBackToTheShippedChain()
    {
        var file = Path.Combine(Path.GetTempPath(), "ccp-ring-cue-" + Guid.NewGuid().ToString("N") + ".wav");
        var service = EmiDeskService.Instance;
        var saved = (CoreModArt.AudioOverridePathProvider, CoreAudio.PlayOneShotProvider, CoreAudio.IsOutputSuppressedProvider,
            CoreSettings.Current.MasterVolume, service.LineEngine, EmiSfx.Clock);
        string? played = null;
        float volume = 0;
        var clock = new CueClock();
        try
        {
            File.WriteAllBytes(file, new byte[] { 0 });  // provider captures the path; no audio device opens
            service.LineEngine = EmiLineEngine.FromJson("{\"moments\":{},\"pools\":{}}");
            CoreSettings.Current.MasterVolume = 50;
            CoreAudio.IsOutputSuppressedProvider = () => false;
            EmiSfx.Clock = clock;
            CoreAudio.PlayOneShotProvider = (path, gain, _, _, _) => { played = path; volume = gain; };
            CoreModArt.AudioOverridePathProvider = rel => rel == "emi/ring_open.mp3" ? file : null;
            EmiSfx.RingOpen();
            Assert.Equal(file, played);
            Assert.Equal(0.085f, volume, 5);
            CoreModArt.AudioOverridePathProvider = _ => null;
            clock.Now = clock.Now.AddSeconds(1);
            EmiSfx.RingOpen();
            Assert.Equal("cards_in.mp3", Path.GetFileName(played));
        }
        finally
        {
            (CoreModArt.AudioOverridePathProvider, CoreAudio.PlayOneShotProvider, CoreAudio.IsOutputSuppressedProvider,
                CoreSettings.Current.MasterVolume, service.LineEngine, EmiSfx.Clock) = saved;
            File.Delete(file);
        }
    }

    private sealed class CueClock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddDays(2);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
