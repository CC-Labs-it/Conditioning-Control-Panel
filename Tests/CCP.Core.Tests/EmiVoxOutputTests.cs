using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Core.Tests;

public class EmiVoxOutputTests
{
    private sealed class Output : IEmiVoxOutput
    {
        public bool IsOutputSuppressed { get; set; }
        public int MasterVolume { get; set; } = 50;
        public bool HasHandle = true;
        public int Stops;
        public readonly List<(string Path, float Gain, string Tag)> Played = new();
        public Action? PlayOneShot(string path, float volume, string tag)
        {
            Assert.True(File.Exists(path));
            Played.Add((path, volume, tag));
            return HasHandle ? () => Stops++ : null;
        }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(50, true)]
    public void OutputMuteAndSuppressionSilenceBothLinesAndTicks(int master, bool suppressed)
    {
        var output = new Output { MasterVolume = master, IsOutputSuppressed = suppressed };
        using var vox = new EmiVox(output);
        Assert.False(vox.Speak("hi again", "idle"));
        Assert.False(vox.Tick());
        Assert.Empty(output.Played);
        Assert.Equal(0, vox.Spoke);
        Assert.Equal(0, vox.Ticks);
    }

    [Fact]
    public void TheHeadReceivesMasterGainAndIndependentTicksWhileStopOwnsOnlyTheLine()
    {
        var output = new Output();
        var vox = new EmiVox(output);
        try
        {
            Assert.True(vox.Speak("hi again", "idle"));
            Assert.True(vox.Tick());
            Assert.Equal(new[] { "emi-vox", "emi-vox-tick" }, output.Played.ConvertAll(p => p.Tag));
            Assert.All(output.Played, p => Assert.Equal(.5f, p.Gain));
            Assert.Equal(0, output.Stops);
            output.MasterVolume = 150;
            Assert.True(vox.Speak("bye again", "sad"));
            Assert.Equal(1f, output.Played[2].Gain);
            Assert.Equal(1, output.Stops);
            vox.Stop();
            Assert.Equal(2, output.Stops);
            vox.Dispose();
            Assert.False(vox.Speak("after close", "idle"));
            Assert.False(vox.Tick());
            Assert.Equal(3, output.Played.Count);
            Assert.Equal(2, output.Stops);
        }
        finally { vox.Dispose(); }
    }

    [Fact]
    public void AMissingNativeHandleKeepsTheWpfLineAndTickReturnValues()
    {
        var output = new Output { HasHandle = false };
        using var vox = new EmiVox(output);
        Assert.False(vox.Speak("hi again", "idle"));
        Assert.True(vox.Tick());
        Assert.Equal(1, vox.Spoke);
        Assert.Equal(1, vox.Ticks);
        vox.Stop();
        Assert.Equal(0, output.Stops);
    }
}
