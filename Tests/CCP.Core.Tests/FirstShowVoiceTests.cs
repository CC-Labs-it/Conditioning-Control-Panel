using System;
using System.Linq;
using ConditioningControlPanel.Services.FirstShow;
using Xunit;

namespace CCP.Core.Tests;

public sealed class FirstShowVoiceTests
{
    // WPF FirstShowAudio.cs:26 uses this exact score, renderer and WAV writer.
    [Fact]
    public void ScoreAndPcmAreDeterministicBoundedAndMoodSensitive()
    {
        const string line = "Hello again, are you ready?";
        var score = FirstShowVoice.MakeScore(line, "idle");
        Assert.NotEmpty(score);
        Assert.Equal(score, FirstShowVoice.MakeScore(line, "idle"));
        Assert.InRange(score.Count, 1, 13);
        Assert.InRange(score[^1].AtMs, 1, 1400);
        var pcm = FirstShowVoice.RenderBurst(score);
        Assert.Contains(pcm, s => s != 0);
        Assert.All(pcm, s => Assert.InRange(s, -1f, 1f));
        var wav = FirstShowVoice.WriteWav(pcm);
        Assert.Equal(wav, FirstShowVoice.WriteWav(FirstShowVoice.RenderBurst(score)));
        Assert.Equal(44 + pcm.Length * 2, wav.Length);
        Assert.Equal(wav.Length - 44, BitConverter.ToInt32(wav, 40));
        Assert.False(pcm.SequenceEqual(FirstShowVoice.RenderBurst(FirstShowVoice.MakeScore(line, "celebration"))));
        Assert.Empty(FirstShowVoice.MakeScore("", "idle"));
    }
}
