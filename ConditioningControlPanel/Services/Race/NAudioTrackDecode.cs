using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ConditioningControlPanel.Services.Race;

/// <summary>
/// WPF's decoder behind <see cref="TrackDecoder.DecodeProvider"/>: MediaFoundationReader first,
/// AudioFileReader as the fallback, then stereo to mono and a WDL resample to 16 kHz.
/// Moved unchanged out of TrackPcm.cs when that file went to CCP.Core.
/// </summary>
internal static class NAudioTrackDecode
{
    /// <summary>Samples pulled per block; big enough that the read loop is not the cost.</summary>
    private const int BlockSamples = 64 * 1024;

    /// <summary>
    /// Decodes <paramref name="path"/> to mono 16 kHz float PCM. Progress is bytes decoded over the
    /// reader's length, 0..1. Cancellation is checked between blocks and throws
    /// <see cref="OperationCanceledException"/>.
    /// </summary>
    public static float[] Decode(string path, IProgress<double>? progress, CancellationToken ct)
    {
        // Media Foundation reads mp3 / m4a / wav / wma out of the box. Formats it refuses (or a
        // machine with the codec pack stripped) fall through to NAudio's own readers.
        WaveStream reader;
        try
        {
            reader = new MediaFoundationReader(path);
        }
        catch (Exception ex)
        {
            Serilog.Log.Information("race-chart: MediaFoundation could not open {Name} ({Message}), using AudioFileReader",
                System.IO.Path.GetFileName(path), ex.Message);
            reader = new AudioFileReader(path);
        }

        using (reader)
        {
            ISampleProvider samples = reader.ToSampleProvider();
            if (samples.WaveFormat.Channels > 1) samples = ToMono(samples);
            if (samples.WaveFormat.SampleRate != TrackPcm.SampleRate)
                samples = new WdlResamplingSampleProvider(samples, TrackPcm.SampleRate);

            long streamLength = 0;
            try { streamLength = reader.Length; }
            catch { /* some readers refuse a length; progress then simply never moves */ }

            var block = new float[BlockSamples];
            var mono = new List<float>(EstimateSamples(reader));
            int read;
            while ((read = samples.Read(block, 0, block.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                for (int i = 0; i < read; i++) mono.Add(block[i]);
                if (progress != null && streamLength > 0)
                {
                    long pos = 0;
                    try { pos = reader.Position; } catch { }
                    progress.Report(Math.Clamp(pos / (double)streamLength, 0, 1));
                }
            }
            progress?.Report(1);

            return mono.ToArray();
        }
    }

    /// <summary>Stereo goes through NAudio's averaging provider; anything wider gets the downmix below.</summary>
    private static ISampleProvider ToMono(ISampleProvider source)
        => source.WaveFormat.Channels == 2
            ? new StereoToMonoSampleProvider(source)
            : new DownmixSampleProvider(source);

    /// <summary>A capacity hint so the sample list does not regrow a hundred times on a long file.</summary>
    private static int EstimateSamples(WaveStream reader)
    {
        try
        {
            double sec = reader.TotalTime.TotalSeconds;
            if (sec > 0 && sec < 24 * 3600) return (int)(sec * TrackPcm.SampleRate) + TrackPcm.SampleRate;
        }
        catch { /* an unknown length is not worth an exception; fall through to the default */ }
        return TrackPcm.SampleRate * 60;
    }

    /// <summary>
    /// Averages any channel count down to mono. StereoToMonoSampleProvider only takes two channels,
    /// and a 5.1 m4a would throw on the way in.
    /// </summary>
    private sealed class DownmixSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly int _channels;
        private float[] _frames = Array.Empty<float>();

        public DownmixSampleProvider(ISampleProvider source)
        {
            _source = source;
            _channels = Math.Max(1, source.WaveFormat.Channels);
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            int wanted = count * _channels;
            if (_frames.Length < wanted) _frames = new float[wanted];
            int read = _source.Read(_frames, 0, wanted);
            int frames = read / _channels;
            for (int f = 0; f < frames; f++)
            {
                float sum = 0;
                for (int c = 0; c < _channels; c++) sum += _frames[f * _channels + c];
                buffer[offset + f] = sum / _channels;
            }
            return frames;
        }
    }
}
