using System;
using System.Linq;
using System.Text;
using ConditioningControlPanel.Services.FirstShow;
using Xunit;

namespace ConditioningControlPanel.Tests;

public class FirstShowVoiceTests
{
    [Fact]
    public void SameLineAndMoodProduceTheSameMelodyAndPcm()
    {
        var a = FirstShowVoice.MakeScore("hi again", "idle");
        var b = FirstShowVoice.MakeScore("hi again", "idle");
        Assert.Equal(a, b);
        Assert.Equal(FirstShowVoice.RenderBurst(a), FirstShowVoice.RenderBurst(b));
        Assert.NotEqual(a.Select(x => x.Pitch), FirstShowVoice.MakeScore("bye again", "idle").Select(x => x.Pitch));
        Assert.True(FirstShowVoice.MakeScore("hi again", "sad")[0].Pitch < a[0].Pitch);
    }

    [Fact]
    public void WpfProsodyAndBurstLimitsArePreserved()
    {
        const string line = "okay okay listen. i know you said one more class but that was four classes ago, sweetheart!";
        foreach (var mood in FirstShowVoice.Moods.Keys)
        {
            var score = FirstShowVoice.MakeScore(line, mood);
            Assert.InRange(score.Count, 1, 13);
            Assert.InRange(score[^1].AtMs, 0, 1400);
            Assert.All(score, b => { Assert.InRange(b.Pitch, .5, 2); Assert.InRange(b.Gain, .02, 1); });
        }
        Assert.True(FirstShowVoice.MakeScore("are you awake?", "idle")[^1].Pitch >
            FirstShowVoice.MakeScore("are you awake", "idle")[^1].Pitch);
        Assert.Empty(FirstShowVoice.MakeScore(null, "idle"));
        Assert.Empty(FirstShowVoice.MakeScore("   ", "idle"));
    }

    [Fact]
    public void BurstIsAudibleBoundedMonoPcmWithSilenceBetweenBlips()
    {
        var score = FirstShowVoice.MakeScore("bye bye. see you at school.", "celebration");
        var samples = FirstShowVoice.RenderBurst(score);
        Assert.True(samples.Length > 44100 * score[^1].AtMs / 1000.0);
        Assert.InRange(samples.Max(x => Math.Abs(x)), .01f, 1f);
        Assert.Equal(0f, samples[2600]); // after the first 56 ms tone, before the next word
        var wav = FirstShowVoice.WriteWav(samples);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal(1, BitConverter.ToUInt16(wav, 20));
        Assert.Equal(1, BitConverter.ToUInt16(wav, 22));
        Assert.Equal(44100u, BitConverter.ToUInt32(wav, 24));
        Assert.Equal(16, BitConverter.ToUInt16(wav, 34));
        Assert.Equal(samples.Length * 2, BitConverter.ToInt32(wav, 40));
        Assert.Equal(44 + samples.Length * 2, wav.Length);
    }
}
