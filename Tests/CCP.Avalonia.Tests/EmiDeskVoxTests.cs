using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class EmiDeskVoxTests
{
    private sealed class Recorder : IEmiVox
    {
        public int Ticks, Stops, Disposals;
        public string? Line, Mood;
        public bool Tick() { Ticks++; return true; }
        public bool Speak(string? text, string? mood) { Line = text; Mood = mood; return true; }
        public void Stop() => Stops++;
        public void Dispose() => Disposals++;
    }

    [Fact]
    public Task SayAndCancellationDriveVoice() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var voice = new Recorder();
        var desk = new EmiDeskWindow { _vox = voice };
        try
        {
            desk.Say("hello", ">_<");
            for (int i = 0; i < 3; i++) desk.ChainPlayer.ElapsePendingHold();
            Assert.Equal(3, voice.Ticks);
            Assert.Equal("hello", voice.Line);
            Assert.Equal(EmiChains.FrameForFace(">_<"), voice.Mood);
            int stops = voice.Stops;
            desk.CancelChain();
            Assert.True(voice.Stops > stops);
            desk.Say("again");
            for (int i = 0; i < 4; i++) desk.ChainPlayer.ElapsePendingHold();
            Assert.True(voice.Stops > stops + 1);
            desk.ShutDown();
            Assert.Equal(1, voice.Disposals);
        }
        finally { desk.ShutDown(); }
        return Task.CompletedTask;
    });

    [Fact]
    public void OutputUsesCoreAudioAndHonorsSilence()
    {
        var previousPlay = CoreAudio.PlayStoppableProvider;
        var previousSuppression = CoreAudio.IsOutputSuppressedProvider;
        int master = CoreSettings.Current.MasterVolume;
        var tags = new List<string>();
        int stops = 0;
        try
        {
            CoreAudio.PlayStoppableProvider = (path, volume, tag, started, finished) => { tags.Add(tag); return () => stops++; };
            CoreAudio.IsOutputSuppressedProvider = () => true;
            CoreSettings.Current.MasterVolume = 100;
            using var voice = new EmiVox(new EmiVoxOutput());
            Assert.False(voice.Speak("hello", "idle"));
            CoreAudio.IsOutputSuppressedProvider = () => false;
            CoreSettings.Current.MasterVolume = 0;
            Assert.False(voice.Speak("hello", "idle"));
            Assert.Empty(tags);
            CoreSettings.Current.MasterVolume = 100;
            Assert.True(voice.Speak("hello", "idle"));
            Assert.Equal(new[] { "emi-vox" }, tags);
            voice.Stop();
            Assert.Equal(1, stops);
        }
        finally
        {
            CoreSettings.Current.MasterVolume = master;
            CoreAudio.PlayStoppableProvider = previousPlay;
            CoreAudio.IsOutputSuppressedProvider = previousSuppression;
        }
    }
}
