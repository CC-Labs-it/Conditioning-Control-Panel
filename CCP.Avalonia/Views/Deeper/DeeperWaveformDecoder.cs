using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Services.Race;
using ConditioningControlPanel.Services.Deeper;
using NAudio.Wave;

namespace ConditioningControlPanel.Avalonia.Views.Deeper;

/// <summary>Audio decoding for the shared waveform cache (WPF MediaDurationProbe.cs:32).</summary>
internal static class DeeperWaveformDecoder
{
    internal static Task<AudioWaveformResult> LoadAsync(string path)
    {
        AudioWaveformCache.Decoder ??= Decode;
        return AudioWaveformCache.LoadAsync(path);
    }

    internal static AudioWaveformResult Decode(string path)
    {
        var ext = Path.GetExtension(path);
        if (ext.Equals(".wav", StringComparison.OrdinalIgnoreCase))
        {
            using var reader = new WaveFileReader(path);
            var format = reader.WaveFormat;
            if ((format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample is 8 or 16 or 24 or 32)
                || (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample is 32 or 64))
            {
                var samples = reader.ToSampleProvider();
                return AudioWaveformCache.BucketPeaks(format.Channels, format.SampleRate,
                    reader.TotalTime.TotalSeconds, buffer => samples.Read(buffer, 0, buffer.Length));
            }
        }
        if (ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase))
        {
            using var reader = new NLayer.MpegFile(path);
            return AudioWaveformCache.BucketPeaks(reader.Channels, reader.SampleRate, reader.Duration.TotalSeconds,
                buffer => reader.ReadSamples(buffer, 0, buffer.Length));
        }

        // Reuse the head's LibVLC decoder for the other formats accepted by the player.
        var decode = TrackDecoder.DecodeProvider ?? RaceTrackPlayer.Decode;
        var pcm = decode(path, null, CancellationToken.None);
        var position = 0;
        return AudioWaveformCache.BucketPeaks(1, TrackPcm.SampleRate, pcm.Length / (double)TrackPcm.SampleRate, buffer =>
        {
            var count = Math.Min(buffer.Length, pcm.Length - position);
            Array.Copy(pcm, position, buffer, 0, count);
            position += count;
            return count;
        });
    }
}
