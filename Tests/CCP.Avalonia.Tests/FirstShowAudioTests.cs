using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Services.EmiDesk;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows.WelcomeShow;
using ConditioningControlPanel.Services.FirstShow;
using Xunit;

namespace CCP.Avalonia.Tests;

// No UI/font setup or audio device: Play records a real WAV and returns a fake stop handle.
public class FirstShowAudioTests
{
    [Fact]
    public void VoiceIsCachedReplacedIndependentlyOfCuesAndDeletedOnDispose()
    {
        var played = new List<(string Path, float Volume, string Tag)>();
        var stopped = new List<string>();
        var audio = new FirstShowAudio { Master = () => 50 };
        audio.Play = (path, volume, tag) => { played.Add((path, volume, tag)); return () => stopped.Add(tag); };
        try
        {
            audio.Speak("hi again", "idle");
            var first = Assert.Single(played);
            Assert.Equal(.5f, first.Volume);
            Assert.Equal("first-show-voice", first.Tag);
            Assert.Equal(EmiVox.WriteWav(EmiVox.RenderBurst(EmiVox.MakeScore("hi again", "idle"))), File.ReadAllBytes(first.Path));
            audio.Cue("pop");
            Assert.Empty(stopped); // a cue must not cut the voice
            Assert.Equal("first-show-cue", played[1].Tag);
            audio.Speak("hi again", "idle");
            Assert.Equal(first.Path, played[2].Path);
            Assert.Single(Directory.GetFiles(Path.GetDirectoryName(first.Path)!));
            Assert.Equal(new[] { "first-show-voice" }, stopped);
            audio.Speak("hi again", "celebration");
            Assert.NotEqual(first.Path, played[3].Path);
            audio.Dispose();
            Assert.Equal(3, stopped.FindAll(x => x == "first-show-voice").Count);
            Assert.Contains("first-show-cue", stopped);
            Assert.False(Directory.Exists(Path.GetDirectoryName(first.Path)));
            audio.Speak("after close"); audio.Cue("pop");
            Assert.Equal(4, played.Count);
        }
        finally { audio.Dispose(); }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(75, true)]
    public void VoiceHonorsMuteAndSuppression(int master, bool suppressed)
    {
        var old = CoreAudio.IsOutputSuppressedProvider;
        using var audio = new FirstShowAudio { Master = () => master };
        int played = 0;
        audio.Play = (_, _, _) => { played++; return () => { }; };
        try
        {
            CoreAudio.IsOutputSuppressedProvider = () => suppressed;
            audio.Speak("hi again"); if (!suppressed) audio.Cue("pop");
            Assert.Equal(0, played);
        }
        finally { CoreAudio.IsOutputSuppressedProvider = old; }
    }
}
