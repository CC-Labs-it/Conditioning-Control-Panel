using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Serilog;

namespace ConditioningControlPanel.Services.Deeper
{
    public class AudioWaveformResult
    {
        public float[] Peaks { get; set; } = Array.Empty<float>();
        public double DurationSeconds { get; set; }
    }

    /// <summary>
    /// Emits a peak-bucket summary of an audio file suitable for timeline rendering.
    /// Caches the result to disk keyed on (path, size, last-write-time) so repeated
    /// opens of the same file are instant. Decoding needs a codec Core cannot reach,
    /// so the head supplies <see cref="Decoder"/> (WPF: NAudio MediaFoundationReader)
    /// and feeds its samples through <see cref="BucketPeaks"/>.
    /// </summary>
    public static class AudioWaveformCache
    {
        private const string CacheMagic = "DPK2";

        public static string CacheFolder => MediaDurationCache.CacheFolder;

        /// <summary>The head's decoder: audio path -> peaks. Throws on decode failure.</summary>
        public static Func<string, AudioWaveformResult>? Decoder { get; set; }

        /// <summary>
        /// Loads peaks for the given audio file, using a disk cache when available.
        /// Throws on decode failure (callers should catch and fall back to a flat strip).
        /// </summary>
        public static Task<AudioWaveformResult> LoadAsync(string audioPath)
        {
            // No ConfigureAwait(false) — the callers are head UI code that
            // assigns the result to DependencyObjects (WaveformPath.Data) on
            // the awaiting thread. Forcing the continuation off the UI thread
            // here previously caused intermittent cross-thread exceptions.
            return Task.Run(() => Load(audioPath));
        }

        public static AudioWaveformResult Load(string audioPath)
        {
            if (!File.Exists(audioPath))
                throw new FileNotFoundException("Audio file not found", audioPath);

            try { Directory.CreateDirectory(CacheFolder); } catch { /* best-effort */ }

            var cacheKey = ComputeCacheKey(audioPath);
            var cachePath = Path.Combine(CacheFolder, cacheKey + ".peaks");

            if (File.Exists(cachePath))
            {
                try
                {
                    var cached = ReadCache(cachePath);
                    if (cached != null) return cached;
                }
                catch (Exception ex)
                {
                    Log.Debug("AudioWaveformCache: cache read failed for {Path}: {Error}", audioPath, ex.Message);
                }
            }

            var fresh = Decode(audioPath);

            try { WriteCache(cachePath, fresh); }
            catch (Exception ex) { Log.Debug("AudioWaveformCache: cache write failed: {Error}", ex.Message); }

            return fresh;
        }

        private static AudioWaveformResult Decode(string audioPath) =>
            (Decoder ?? throw new InvalidOperationException("AudioWaveformCache: no decoder registered"))(audioPath);

        /// <summary>
        /// The bucket rule: 6 peaks a second clamped to 64..4096, each the loudest
        /// absolute sample (any channel) in its slice. <paramref name="read"/> fills the
        /// buffer with interleaved float samples and returns how many it wrote (0 = end).
        /// </summary>
        public static AudioWaveformResult BucketPeaks(int channels, int sampleRate, double durationSec, Func<float[], int> read)
        {
            int peakCount = Math.Clamp((int)(durationSec * 6), 64, 4096);
            var peaks = new float[peakCount];

            if (durationSec <= 0 || sampleRate <= 0 || channels <= 0)
                return new AudioWaveformResult { Peaks = peaks, DurationSeconds = durationSec };

            double framesPerBucket = (durationSec * sampleRate) / peakCount;
            if (framesPerBucket <= 0) framesPerBucket = 1;

            var buf = new float[8192];
            int currentBucket = 0;
            double frameInBucket = 0;
            float currentMax = 0;

            while (currentBucket < peakCount)
            {
                int n = read(buf);
                if (n == 0) break;
                int frames = n / channels;
                for (int f = 0; f < frames; f++)
                {
                    float frameMax = 0;
                    int baseIx = f * channels;
                    for (int c = 0; c < channels; c++)
                    {
                        var v = Math.Abs(buf[baseIx + c]);
                        if (v > frameMax) frameMax = v;
                    }
                    if (frameMax > currentMax) currentMax = frameMax;
                    frameInBucket++;
                    if (frameInBucket >= framesPerBucket)
                    {
                        peaks[currentBucket++] = currentMax;
                        currentMax = 0;
                        frameInBucket -= framesPerBucket;
                        if (currentBucket >= peakCount) break;
                    }
                }
            }
            while (currentBucket < peakCount) peaks[currentBucket++] = currentMax;

            return new AudioWaveformResult { Peaks = peaks, DurationSeconds = durationSec };
        }

        /// <summary>A <see cref="Decoder"/> for heads without NAudio (Avalonia: LibVLC): the race's
        /// mono 16 kHz decode (<see cref="Race.TrackDecoder"/>) through <see cref="BucketPeaks"/>.</summary>
        public static AudioWaveformResult DecodeViaTrackDecoder(string audioPath) =>
            FromMono(Race.TrackDecoder.Decode(audioPath, null, System.Threading.CancellationToken.None).Mono16k, Race.TrackPcm.SampleRate);

        /// <summary>Mono float PCM -> peaks, by the same bucket rule as the WPF reader.</summary>
        public static AudioWaveformResult FromMono(float[] pcm, int sampleRate)
        {
            int pos = 0;
            return BucketPeaks(1, sampleRate, pcm.Length / (double)sampleRate, buf =>
            {
                int n = Math.Min(buf.Length, pcm.Length - pos);
                Array.Copy(pcm, pos, buf, 0, n);
                pos += n;
                return n;
            });
        }

        private static string ComputeCacheKey(string audioPath)
        {
            var info = new FileInfo(audioPath);
            var key = $"{audioPath.ToLowerInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
            using var sha = SHA1.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private static AudioWaveformResult? ReadCache(string cachePath)
        {
            using var fs = File.OpenRead(cachePath);
            using var br = new BinaryReader(fs);
            var magic = new string(br.ReadChars(4));
            if (magic != CacheMagic) return null;
            var duration = br.ReadDouble();
            var count = br.ReadInt32();
            if (count <= 0 || count > 16384) return null;
            var peaks = new float[count];
            for (int i = 0; i < count; i++) peaks[i] = br.ReadSingle();
            return new AudioWaveformResult { Peaks = peaks, DurationSeconds = duration };
        }

        private static void WriteCache(string cachePath, AudioWaveformResult result)
        {
            using var fs = File.Create(cachePath);
            using var bw = new BinaryWriter(fs);
            bw.Write(CacheMagic.ToCharArray());
            bw.Write(result.DurationSeconds);
            bw.Write(result.Peaks.Length);
            foreach (var p in result.Peaks) bw.Write(p);
        }
    }
}
