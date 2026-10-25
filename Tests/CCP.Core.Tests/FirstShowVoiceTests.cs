using System;
using System.Linq;
using System.Text;
using ConditioningControlPanel.Services.FirstShow;
using Xunit;

namespace ConditioningControlPanel.Tests;

public sealed class FirstShowVoiceTests
{
    [Fact]
    public void WpfVoicePreservesMoodQuestionLiftAndBoundedPace()
    {
        var idle = FirstShowVoice.MakeScore("good girl", "idle");
        Assert.Equal(idle, FirstShowVoice.MakeScore("good girl", "unknown"));
        Assert.True(FirstShowVoice.MakeScore("good girl", "sad")[0].Pitch < idle[0].Pitch);
        Assert.True(FirstShowVoice.MakeScore("are you still awake in there?", "idle")[^1].Pitch >
                    FirstShowVoice.MakeScore("are you still awake in there", "idle")[^1].Pitch);
        var longLine = FirstShowVoice.MakeScore(string.Join(" ", Enumerable.Repeat("sweetheart listen carefully.", 40)), "celebration");
        Assert.InRange(longLine.Count, 1, FirstShowVoice.VoxDials.MaxBlips);
        Assert.InRange(longLine[^1].AtMs, 0, FirstShowVoice.VoxDials.BurstMaxMs);
        Assert.Empty(FirstShowVoice.MakeScore("  ", "idle"));
    }

    [Fact]
    public void SpeechIsDeterministicAudibleMonoPcmWithSilentGaps()
    {
        byte[] Render() => FirstShowVoice.WriteWav(FirstShowVoice.RenderBurst(FirstShowVoice.MakeScore("hello again", "celebration")));
        var wav = Render();
        Assert.Equal(wav, Render());
        Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal(44100, BitConverter.ToInt32(wav, 24));
        Assert.Equal(1, BitConverter.ToInt16(wav, 22));
        Assert.Equal(16, BitConverter.ToInt16(wav, 34));
        Assert.Equal(wav.Length - 44, BitConverter.ToInt32(wav, 40));
        var samples = FirstShowVoice.RenderBurst(FirstShowVoice.MakeScore("hello again", "celebration"));
        Assert.Contains(samples, s => s != 0);
        Assert.Contains(samples, s => s == 0);
        Assert.All(samples, s => Assert.InRange(s, -1, 1));
    }
}
