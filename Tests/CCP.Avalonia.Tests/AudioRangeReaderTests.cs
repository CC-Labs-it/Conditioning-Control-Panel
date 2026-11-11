using System;
using System.IO;
using System.Linq;
using System.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class AudioRangeReaderTests
{
    [Fact]
    public void ReadsOnlyTheRequestedRangeAsMono44100()
    {
        WithDummyVlc(() =>
        {
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
        });
    }

    [Fact]
    public void DummyVlcScopeRestoresThePreviousInstanceEvenOnFailure()
    {
        var original = LibVlcAudio.Shared;
        try
        {
            LibVlcAudio.Shared = null;
            LibVLCSharp.Shared.LibVLC? temporary = null;
            Assert.Throws<InvalidOperationException>(() => WithDummyVlc(() =>
            {
                temporary = LibVlcAudio.Shared;
                throw new InvalidOperationException();
            }));
            Assert.True(ReferenceEquals(null, LibVlcAudio.Shared));
            Assert.NotNull(temporary);
            Assert.Equal(IntPtr.Zero, temporary.NativeReference);

            var previous = new LibVlcAudio("--aout=dummy");
            try
            {
                Assert.Same(previous.Vlc, LibVlcAudio.Shared);
                WithDummyVlc(() =>
                {
                    temporary = LibVlcAudio.Shared;
                    Assert.NotSame(previous.Vlc, temporary);
                });
                Assert.Same(previous.Vlc, LibVlcAudio.Shared);
                Assert.NotNull(temporary);
                Assert.Equal(IntPtr.Zero, temporary.NativeReference);
            }
            finally { LibVlcAudio.Shared = original; previous.Vlc.Dispose(); }
        }
        finally { LibVlcAudio.Shared = original; }
    }

    private static void WithDummyVlc(Action run)
    {
        var previous = LibVlcAudio.Shared;
        var audio = new LibVlcAudio("--aout=dummy");
        try
        {
            LibVlcAudio.Shared = audio.Vlc;
            run();
        }
        finally
        {
            LibVlcAudio.Shared = previous;
            audio.Vlc.Dispose();
        }
    }
}
