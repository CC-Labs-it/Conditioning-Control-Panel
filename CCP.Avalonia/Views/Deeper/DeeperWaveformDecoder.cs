using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Services.Deeper;
using NAudio.Wave;

namespace ConditioningControlPanel.Avalonia.Views.Deeper;

/// <summary>Audio decoding for the shared waveform cache (WPF MediaDurationProbe.cs:32).</summary>
internal static class DeeperWaveformDecoder
{
    internal static Action<string, string, CancellationToken> Transcode =
        (source, dest, token) => RaceTrackPlayer.TranscodeToWav(source, dest, null, token, preserveChannels: true);

    internal static Task<AudioWaveformResult> LoadAsync(string path, CancellationToken token) =>
        AudioWaveformCache.LoadAsync(path, p => Decode(p, token), token, cacheVersion: 1);

    internal static AudioWaveformResult Decode(string path) => Decode(path, CancellationToken.None);

    internal static AudioWaveformResult Decode(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var ext = Path.GetExtension(path);
        if (ext.Equals(".wav", StringComparison.OrdinalIgnoreCase))
        {
            using var reader = new WaveFileReader(path);
            var format = reader.WaveFormat;
            if ((format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample is 8 or 16 or 24 or 32)
                || (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample is 32 or 64))
                return ReadPeaks(reader, token);
        }
        if (ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase))
        {
            using var reader = new NLayer.MpegFile(path);
            return AudioWaveformCache.BucketPeaks(reader.Channels, reader.SampleRate, reader.Duration.TotalSeconds,
                buffer => { token.ThrowIfCancellationRequested(); return reader.ReadSamples(buffer, 0, buffer.Length); });
        }
        var wav = Path.Combine(Path.GetTempPath(), "ccp-wave-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            Transcode(path, wav, token);
            token.ThrowIfCancellationRequested();
            using var reader = new WaveFileReader(wav);
            return ReadPeaks(reader, token);
        }
        finally { try { File.Delete(wav); } catch { } }
    }

    private static AudioWaveformResult ReadPeaks(WaveFileReader reader, CancellationToken token)
    {
        var format = reader.WaveFormat.AsStandardWaveFormat();
        // WaveFileReader exposes extensible fmt chunks as WaveFormatExtraData.
        if (format.Encoding == WaveFormatEncoding.Extensible && format is WaveFormatExtraData extra && format.ExtraSize >= 22)
        {
            var subtype = new Guid(extra.ExtraData.AsSpan(6, 16));
            if (subtype == NAudio.Dmo.AudioMediaSubtypes.MEDIASUBTYPE_PCM)
                format = new WaveFormat(format.SampleRate, format.BitsPerSample, format.Channels);
            else if (subtype == NAudio.Dmo.AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT)
                format = WaveFormat.CreateIeeeFloatWaveFormat(format.SampleRate, format.Channels);
        }
        using var normalized = new RawSourceWaveStream(reader, format);
        var samples = normalized.ToSampleProvider();
        return AudioWaveformCache.BucketPeaks(reader.WaveFormat.Channels, reader.WaveFormat.SampleRate,
            reader.TotalTime.TotalSeconds, buffer =>
            {
                token.ThrowIfCancellationRequested();
                var count = buffer.Length - buffer.Length % reader.WaveFormat.Channels;
                return samples.Read(buffer, 0, count);
            });
    }
}
