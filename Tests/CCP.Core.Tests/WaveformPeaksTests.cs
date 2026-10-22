using System;
using System.IO;
using ConditioningControlPanel.Services.Deeper;
using ConditioningControlPanel.Services.Race;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>WPF Services/Deeper/AudioWaveformCache.cs as Core WaveformPeaks: 6 buckets a second
/// (64..4096), max |sample| per bucket, and a disk cache that skips the second decode.</summary>
public sealed class WaveformPeaksTests
{
    [Fact]
    public void BucketsSixASecondKeepingTheLoudestSample()
    {
        var mono = new float[TrackPcm.SampleRate * 20];          // 20 s -> 120 buckets
        mono[TrackPcm.SampleRate * 10 + 5] = -0.75f;              // bucket 60
        var peaks = WaveformPeaks.Bucket(mono, TrackPcm.SampleRate);
        Assert.Equal(120, peaks.Length);
        Assert.Equal(0.75f, peaks[60]);
        Assert.Equal(0f, peaks[59]);
        Assert.Equal(0f, peaks[61]);

        Assert.Equal(64, WaveformPeaks.Bucket(new float[TrackPcm.SampleRate], TrackPcm.SampleRate).Length);
    }

    [Fact]
    public void SecondLoadComesFromTheCache()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-wave-").FullName;
        var path = Path.Combine(dir, "clip.mp3");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
        var mono = new float[TrackPcm.SampleRate * 20];
        mono[0] = 0.5f;
        int decodes = 0;
        try
        {
            var first = WaveformPeaks.Load(path, _ => { decodes++; return mono; });
            var second = WaveformPeaks.Load(path, _ => { decodes++; return mono; });
            Assert.Equal(1, decodes);
            Assert.Equal(first, second);
            Assert.Equal(0.5f, second[0]);
        }
        finally { Directory.Delete(dir, true); }
    }
}
