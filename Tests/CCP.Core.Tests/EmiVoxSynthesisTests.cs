using System;
using System.Linq;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Core.Tests;

// Behaviour assertions from WPF EmiVoxScoreTests; no audio device or profile.
public class EmiVoxSynthesisTests
{
    private const string Long =
        "okay okay listen. i know you said one more class but that was four classes ago, sweetheart!";

    [Fact]
    public void RepresentativeScoreMatchesFrozenWpfAndWavIsDeterministic()
    {
        var score = EmiVoxSynthesis.MakeScore("hi again", "idle");
        Assert.Equal(new[] { 0, 120, 190 }, score.Select(b => b.AtMs));
        var pitches = new[] { .889025367765673, .873044150464848, .8096992983822172 };
        for (int i = 0; i < score.Count; i++)
        {
            Assert.Equal(pitches[i], score[i].Pitch, 12);
            Assert.Equal(.4, score[i].Gain, 12);
        }
        Assert.Equal(EmiVoxSynthesis.WriteWav(EmiVoxSynthesis.RenderBurst(score)),
            EmiVoxSynthesis.WriteWav(EmiVoxSynthesis.RenderBurst(EmiVoxSynthesis.MakeScore("hi again", "idle"))));
    }

    [Fact]
    public void TheSameLineAlwaysSoundsLikeItself()
    {
        var a = EmiVoxSynthesis.MakeScore("hi again", "idle");
        var b = EmiVoxSynthesis.MakeScore("hi again", "idle");
        Assert.Equal(a, b);
    }

    [Fact]
    public void TwoDifferentLinesAreTwoDifferentMelodies()
    {
        var a = EmiVoxSynthesis.MakeScore("hi again", "idle");
        var b = EmiVoxSynthesis.MakeScore("bye again", "idle");
        Assert.NotEqual(a.Select(x => x.Pitch), b.Select(x => x.Pitch));
    }

    [Fact]
    public void AMoodChangesTheVoiceWithoutChangingTheWords()
    {
        var idle = EmiVoxSynthesis.MakeScore("good girl", "idle");
        var sad = EmiVoxSynthesis.MakeScore("good girl", "sad");
        Assert.Equal(idle.Count, sad.Count);
        Assert.True(sad[0].Pitch < idle[0].Pitch, "sad is the lowest mood and must sit under idle");
    }

    [Fact]
    public void ALongLineIsCompressedIntoBothCeilings()
    {
        var score = EmiVoxSynthesis.MakeScore(Long, "idle");
        Assert.NotEmpty(score);
        Assert.True(score.Count <= EmiVoxSynthesis.VoxDials.MaxBlips,
            $"{score.Count} blips is over the ceiling of {EmiVoxSynthesis.VoxDials.MaxBlips}");
        Assert.True(score[^1].AtMs <= EmiVoxSynthesis.VoxDials.BurstMaxMs,
            $"the burst ran {score[^1].AtMs} ms, over the {EmiVoxSynthesis.VoxDials.BurstMaxMs} ms ceiling");
    }

    [Fact]
    public void NoBlipIsEverSentOutsideThePitchWindow()
    {
        foreach (var mood in EmiVoxSynthesis.Moods.Keys)
        {
            foreach (var line in new[] { Long, "wait, what?", "YES!", "oh... never mind", "hi" })
            {
                foreach (var b in EmiVoxSynthesis.MakeScore(line, mood))
                {
                    Assert.InRange(b.Pitch, EmiVoxSynthesis.VoxDials.PitchMin, EmiVoxSynthesis.VoxDials.PitchMax);
                    Assert.InRange(b.Gain, 0.02, 1.0);
                }
            }
        }
    }

    [Fact]
    public void AQuestionRises()
    {
        var asked = EmiVoxSynthesis.MakeScore("are you still awake in there", "idle");
        var told = EmiVoxSynthesis.MakeScore("are you still awake in there?", "idle");
        Assert.Equal(asked.Count, told.Count);
        Assert.True(told[^1].Pitch > asked[^1].Pitch, "the question mark did not lift the tail");
    }

    [Fact]
    public void NothingToSayIsSilence()
    {
        Assert.Empty(EmiVoxSynthesis.MakeScore(null, "idle"));
        Assert.Empty(EmiVoxSynthesis.MakeScore("   ", "idle"));
    }

    [Fact]
    public void SyllablesAreCountedTheWayTheCampusCountsThem()
    {
        Assert.Equal(1, EmiVoxSynthesis.Syllables("hi"));
        Assert.Equal(2, EmiVoxSynthesis.Syllables("hello"));
        Assert.Equal(1, EmiVoxSynthesis.Syllables("!!!"));
        Assert.Equal(1, EmiVoxSynthesis.Syllables(""));
        Assert.True(EmiVoxSynthesis.Syllables("responsibilities") <= 4);
    }

    [Fact]
    public void TheRenderedBurstHasAudioInIt()
    {
        var score = EmiVoxSynthesis.MakeScore("bye bye. see you at school.", "celebration");
        Assert.NotEmpty(score);

        var buf = EmiVoxSynthesis.RenderBurst(score);
        Assert.True(buf.Length > 44100 * (score[^1].AtMs / 1000.0),
            "the buffer ended before the last blip did");

        double peak = buf.Aggregate(0.0, (a, s) => Math.Max(a, Math.Abs(s)));
        Assert.True(peak > 0.01, "the burst rendered silence");
        Assert.True(peak <= 1.0, "the burst clipped");
    }

    [Fact]
    public void TheWavHeaderIsPlainMonoPcm()
    {
        var wav = EmiVoxSynthesis.WriteWav(EmiVoxSynthesis.RenderBurst(EmiVoxSynthesis.MakeScore("hi", "idle")));
        Assert.True(wav.Length > 44);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal(1, BitConverter.ToUInt16(wav, 20));      // PCM
        Assert.Equal(1, BitConverter.ToUInt16(wav, 22));      // mono
        Assert.Equal(44100u, BitConverter.ToUInt32(wav, 24));
        Assert.Equal(16, BitConverter.ToUInt16(wav, 34));
    }
}
