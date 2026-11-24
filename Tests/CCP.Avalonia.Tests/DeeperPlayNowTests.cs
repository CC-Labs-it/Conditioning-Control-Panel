using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class DeeperPlayNowTests
{
    private sealed class Audio : IDeeperLocalAudio
    {
        public double DurationSeconds => 10;
        public double? PlaybackDurationSeconds => null;
        public double PositionSeconds { get; set; }
        public int Volume { get; set; }
        public bool Playing;
        public event Action? Ended { add { } remove { } }
        public event Action? Failed { add { } remove { } }
        public void Play() => Playing = true;
        public void Pause() => Playing = false;
        public void Dispose() { }
    }

    [Fact]
    public Task WaitsForRequestedFileThenStartsItsAudio() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var settings = CoreSettings.Current;
        var bounds = (settings.DeeperPlayerWindowLeft, settings.DeeperPlayerWindowTop, settings.DeeperPlayerWindowWidth, settings.DeeperPlayerWindowHeight, settings.DeeperPlayerWindowPositionHint);
        var media = Path.GetTempFileName();
        var oldOpen = DeeperLocalAudio.Open;
        var audio = new Audio();
        DeeperLocalAudio.Open = _ => Task.FromResult<IDeeperLocalAudio?>(audio);
        var window = new EnhancementPlayerWindow(new Enhancement { MediaType = MediaTypes.Audio, MediaSource = media }, "test")
            { LoadWaveformPeaks = DeeperPlayerAudioTests.NoPeaks };
        try
        {
            window.Show();
            var loaded = typeof(EnhancementPlayerWindow).GetProperty("LoadedFilePath")!;
            loaded.SetValue(window, "other.ccpenh.json");
            window.MaximizeAndPlayWhenReady("chosen.ccpenh.json");
            window.PlayNowTick();
            Assert.Equal(WindowState.Maximized, window.WindowState);
            Assert.False(audio.Playing);
            loaded.SetValue(window, "chosen.ccpenh.json");
            window.PlayNowTick();
            Dispatcher.UIThread.RunJobs();
            Assert.True(audio.Playing);
        }
        finally
        {
            window.Close(); DeeperLocalAudio.Open = oldOpen; File.Delete(media);
            (settings.DeeperPlayerWindowLeft, settings.DeeperPlayerWindowTop, settings.DeeperPlayerWindowWidth, settings.DeeperPlayerWindowHeight, settings.DeeperPlayerWindowPositionHint) = bounds;
        }
        return Task.CompletedTask;
    });
}
