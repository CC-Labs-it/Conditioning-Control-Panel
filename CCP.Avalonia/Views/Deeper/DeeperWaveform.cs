using System;
using System.Threading;
using ConditioningControlPanel.Services.Deeper;
using ConditioningControlPanel.Services.Race;

namespace ConditioningControlPanel.Avalonia.Views.Deeper;

internal static class DeeperWaveform
{
    // WPF Services/Deeper/AudioWaveformCache.cs:92: head decodes, Core buckets and caches.
    internal static AudioWaveformResult Decode(string path)
    {
        var decode = TrackDecoder.DecodeProvider ?? throw new InvalidOperationException("Audio decoder unavailable");
        var samples = decode(path, null, CancellationToken.None);
        int position = 0;
        return AudioWaveformCache.BucketPeaks(1, TrackPcm.SampleRate,
            samples.Length / (double)TrackPcm.SampleRate, buffer =>
            {
                int count = Math.Min(buffer.Length, samples.Length - position);
                Array.Copy(samples, position, buffer, 0, count);
                position += count;
                return count;
            });
    }
}
