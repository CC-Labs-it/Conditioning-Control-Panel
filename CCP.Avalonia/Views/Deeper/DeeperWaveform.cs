using System;
using System.IO;
using System.Threading;
using ConditioningControlPanel.Services.Deeper;
using ConditioningControlPanel.Avalonia.Views.Games;

namespace ConditioningControlPanel.Avalonia.Views.Deeper;

internal static class DeeperWaveform
{
    internal static Action<string, string> Transcode = (source, dest) =>
        RaceTrackPlayer.TranscodeToWav(source, dest, null, CancellationToken.None, preserveFormat: true);
    // WPF MediaDurationProbe.cs:37: retain source channels and rate for Core's max-absolute buckets.
    internal static AudioWaveformResult Decode(string path)
    {
        var wav = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wav");
        try { Transcode(path, wav); return Read(wav); }
        finally { File.Delete(wav); }
    }
    private static AudioWaveformResult Read(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        if (reader.ReadUInt32() != 0x46464952) throw new InvalidDataException("Expected RIFF");
        reader.ReadUInt32();
        if (reader.ReadUInt32() != 0x45564157) throw new InvalidDataException("Expected WAVE");
        int channels = 0, rate = 0;
        while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
        {
            uint id = reader.ReadUInt32(), size = reader.ReadUInt32();
            long end = reader.BaseStream.Position + size;
            if (id == 0x20746D66 && size >= 16 && end <= reader.BaseStream.Length)
            {
                var pcm = reader.ReadUInt16(); channels = reader.ReadUInt16(); rate = reader.ReadInt32();
                reader.ReadBytes(6);
                if (pcm != 1 || reader.ReadUInt16() != 16) throw new InvalidDataException("Expected PCM16");
            }
            else if (id == 0x61746164 && channels > 0 && rate > 0)
            {
                end = size == 0 ? reader.BaseStream.Length : Math.Min(end, reader.BaseStream.Length);
                return AudioWaveformCache.BucketPeaks(channels, rate,
                    (end - reader.BaseStream.Position) / (double)(channels * 2) / rate, buffer =>
                    {
                        int count = (int)Math.Min(buffer.Length / channels * channels, (end - reader.BaseStream.Position) / 2 / channels * channels);
                        for (int i = 0; i < count; i++) buffer[i] = reader.ReadInt16() / 32768f;
                        return count;
                    });
            }
            if (end > reader.BaseStream.Length) break;
            reader.BaseStream.Position = end + (size & 1);
        }
        throw new InvalidDataException("Missing PCM data");
    }
}
