using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.Speech;
using Xunit;

namespace CCP.Core.Tests;

public sealed class SherpaWakeTests
{
    [Fact]
    public async Task MissingModelAndMicrophoneNeverOpenCapture()
    {
        using var rig = new Rig();
        File.Delete(Path.Combine(rig.Root, "tokens.txt"));
        Assert.False(rig.Wake.IsConfigured);
        Assert.False(await rig.Wake.WaitForWakeAsync(default));
        Assert.Contains("isn't installed", (await rig.Wake.CalibrateAsync()).Message);
        File.WriteAllText(Path.Combine(rig.Root, "tokens.txt"), "test");
        rig.Mic.HasDevice = false;
        Assert.Equal("No microphone detected.", (await rig.Wake.CalibrateAsync()).Message);
        Assert.Equal(0, rig.Mic.Starts);
    }

    [Fact]
    public void NestedModelRequiresAllFilesAndPrefersInt8()
    {
        using var rig = new Rig();
        File.WriteAllText(Path.Combine(rig.Root, "encoder.int8.onnx"), "test");
        Assert.EndsWith("encoder.int8.onnx", SherpaWakeService.FindModel(rig.Root)!.Value.Encoder);
        File.Delete(Path.Combine(rig.Root, "joiner.onnx"));
        Assert.Null(SherpaWakeService.FindModel(rig.Root));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FiveSaysUseStrictestSuccessfulThresholdAndKeepRoomToneSilent(bool noisy)
    {
        using var rig = new Rig();
        rig.Mic.Feed = onPcm => FeedSays(onPcm, 5);
        rig.Wake.CalibrationScorer = (threshold, audio) =>
            audio.Count(x => Math.Abs(x) > 0.01) >= 4000 ? threshold <= 0.20 : noisy && threshold >= 0.20;
        var progress = new List<SherpaWakeService.CalibrationProgress>();
        var result = await rig.Wake.CalibrateAsync(5, new InlineProgress(progress.Add));
        Assert.True(result.Success);
        Assert.Equal(noisy ? 0.16 : 0.20, result.ChosenThreshold);
        Assert.Equal(5, result.Utterances);
        Assert.Equal(5, result.CaughtAtChosen);
        Assert.Equal(result.ChosenThreshold, CoreSettings.Current.SpeechWakeThreshold);
        Assert.Contains(progress, p => p.Phase == "analyze" && p.Captured == 5 && p.Target == 5);
        Assert.Equal(new[] { true, false }, rig.Listening);
        Assert.Equal(1, rig.Mic.Stops);
    }

    [Fact]
    public async Task RecallFallbackWarnsWhenAmbientAlsoFires()
    {
        using var rig = new Rig();
        rig.Mic.Feed = onPcm => FeedSays(onPcm, 5);
        rig.Wake.CalibrationScorer = (threshold, _) => threshold <= 0.13;
        var result = await rig.Wake.CalibrateAsync();
        Assert.True(result.Success);
        Assert.Equal(0.13, result.ChosenThreshold);
        Assert.Contains("recall-biased", result.Message);
    }

    [Fact]
    public async Task FailedSweepDoesNotChangeSensitivity()
    {
        using var rig = new Rig();
        rig.Mic.Feed = onPcm => FeedSays(onPcm, 5);
        rig.Wake.CalibrationScorer = (_, _) => false;
        var result = await rig.Wake.CalibrateAsync();
        Assert.False(result.Success);
        Assert.Contains("didn't change anything", result.Message);
        Assert.Equal(rig.SavedThreshold, CoreSettings.Current.SpeechWakeThreshold);
    }

    [Fact]
    public async Task BusyAndPrivacyStopCloseCalibrationWithoutSaving()
    {
        using var rig = new Rig();
        var active = rig.Wake.CalibrateAsync();
        Assert.True(rig.Wake.IsListening);
        Assert.Contains("busy", (await rig.Wake.CalibrateAsync()).Message);
        rig.Wake.StopListening();
        var result = await active.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(result.Success);
        Assert.Equal("Calibration cancelled.", result.Message);
        Assert.False(rig.Wake.IsListening);
        Assert.Equal(1, rig.Mic.Stops);
        Assert.Equal(rig.SavedThreshold, CoreSettings.Current.SpeechWakeThreshold);
    }

    [Fact]
    public async Task BestEffortKeepsTwoMatchesAndAsksForABetterFit()
    {
        using var rig = new Rig();
        rig.Mic.Feed = onPcm => FeedSays(onPcm, 5);
        int scored = 0;
        rig.Wake.CalibrationScorer = (_, audio) => audio.Any(x => Math.Abs(x) > 0.01) && ++scored % 5 < 2;
        var result = await rig.Wake.CalibrateAsync();
        Assert.True(result.Success);
        Assert.Equal(2, result.CaughtAtChosen);
        Assert.Contains("Re-run for a better fit", result.Message);
    }

    [Fact]
    public async Task CancellationDuringSweepDoesNotSaveANewThreshold()
    {
        using var rig = new Rig();
        rig.Mic.Feed = onPcm => FeedSays(onPcm, 5);
        rig.Wake.CalibrationScorer = (_, _) => { rig.Wake.StopListening(); return true; };
        Assert.False((await rig.Wake.CalibrateAsync()).Success);
        Assert.Equal(rig.SavedThreshold, CoreSettings.Current.SpeechWakeThreshold);
    }

    private static void FeedSays(Action<byte[], int> feed, int count)
    {
        void Frames(int frames, short amplitude)
        {
            var pcm = new byte[1600];
            for (int j = 0; j < 800; j++)
            {
                var sample = (short)(j % 2 == 0 ? amplitude : -amplitude);
                pcm[j * 2] = (byte)sample; pcm[j * 2 + 1] = (byte)(sample >> 8);
            }
            for (int i = 0; i < frames; i++) feed(pcm, pcm.Length);
        }
        Frames(20, 30); // room-tone seed + usable ambient (>0.5 s)
        Frames(2, 3000); Frames(20, 30); // reject a 100 ms transient before the five valid says
        for (int i = 0; i < count; i++) { Frames(8, 3000); Frames(20, 30); }
    }

    private sealed class InlineProgress(Action<SherpaWakeService.CalibrationProgress> report)
        : IProgress<SherpaWakeService.CalibrationProgress>
    { public void Report(SherpaWakeService.CalibrationProgress value) => report(value); }

    private sealed class Mic : IMicSource
    {
        public bool HasDevice { get; set; } = true;
        internal int Starts, Stops;
        internal Action<Action<byte[], int>>? Feed;
        public IReadOnlyList<SpeechInputDevice> ListDevices() => Array.Empty<SpeechInputDevice>();
        public IDisposable Start(Action<byte[], int> onPcm) { Starts++; Feed?.Invoke(onPcm); return new Stop(() => Stops++); }
        private sealed class Stop(Action stop) : IDisposable { public void Dispose() => stop(); }
    }

    private sealed class Rig : IDisposable
    {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "ccp-wake-" + Guid.NewGuid().ToString("N"));
        internal readonly Mic Mic = new();
        internal readonly SherpaWakeService Wake;
        internal readonly double SavedThreshold = CoreSettings.Current.SpeechWakeThreshold;
        internal readonly List<bool> Listening = new();
        internal Rig()
        {
            Directory.CreateDirectory(Root);
            foreach (var name in new[] { "encoder.onnx", "decoder.onnx", "joiner.onnx", "tokens.txt", "keywords.txt" })
                File.WriteAllText(Path.Combine(Root, name), "test");
            Wake = new SherpaWakeService(Mic, Root);
            Wake.ListeningChanged += (_, value) => Listening.Add(value);
        }
        public void Dispose()
        {
            Wake.Dispose(); CoreSettings.Current.SpeechWakeThreshold = SavedThreshold;
            Directory.Delete(Root, true);
        }
    }
}
