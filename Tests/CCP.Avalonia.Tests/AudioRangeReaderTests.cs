using System;
using System.IO;
using System.Linq;
using System.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class AudioRangeReaderTests
{
    [Fact]
    public void ReadsOnlyTheRequestedRangeAsMono44100()
    {
        _ = new LibVlcAudio("--aout=dummy");
        var file = Path.Combine(Path.GetTempPath(), "ccp-range-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            using (var writer = new BinaryWriter(File.Create(file)))
            {
                const int frames = 44100 * 3;
                writer.Write("RIFF"u8); writer.Write(36 + frames * 4); writer.Write("WAVE"u8);
                writer.Write("fmt "u8); writer.Write(16); writer.Write((short)1); writer.Write((short)2);
                writer.Write(44100); writer.Write(44100 * 4); writer.Write((short)4); writer.Write((short)16);
                writer.Write("data"u8); writer.Write(frames * 4);
                for (int i = 0; i < frames; i++)
                {
                    short sample = (short)(i >= 44100 && i < 88200 ? 12000 : 0);
                    writer.Write(sample); writer.Write(sample);
                }
            }
            var samples = AudioRangeReader.Read(file, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), CancellationToken.None);
            Assert.InRange(samples.Length, 41000, 45000);
            Assert.InRange(samples.Average(x => Math.Abs(x)), 0.3, 0.4);
        }
        finally { File.Delete(file); }
    }
}
