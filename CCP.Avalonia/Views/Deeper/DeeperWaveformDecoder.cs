using System.Threading.Tasks;
using ConditioningControlPanel.Services.Deeper;
using NAudio.Wave;

namespace ConditioningControlPanel.Avalonia.Views.Deeper;

/// <summary>WAV decoding for the shared waveform cache (WPF MediaDurationProbe.cs:32).</summary>
internal static class DeeperWaveformDecoder
{
    internal static Task<AudioWaveformResult> LoadAsync(string path)
    {
        AudioWaveformCache.Decoder ??= Decode;
        return AudioWaveformCache.LoadAsync(path);
    }

    internal static AudioWaveformResult Decode(string path)
    {
        using var reader = new WaveFileReader(path);
        var samples = reader.ToSampleProvider();
        return AudioWaveformCache.BucketPeaks(reader.WaveFormat.Channels, reader.WaveFormat.SampleRate,
            reader.TotalTime.TotalSeconds, buffer => samples.Read(buffer, 0, buffer.Length));
    }
}
