using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Race;
using Serilog;

namespace ConditioningControlPanel.Services.Deeper
{
    /// <summary>
    /// The Deeper editor's waveform peaks: WPF Services/Deeper/AudioWaveformCache.cs with the decode
    /// going through the head's <see cref="TrackDecoder.DecodeProvider"/> (mono 16 kHz) instead of
    /// NAudio. Same bucket count (6 per second, 64..4096), same disk cache file and format ("DPK2"),
    /// so a cached file is not decoded twice. A new name so it cannot collide with the WPF class (P25).
    /// </summary>
    public static class WaveformPeaks
    {
        private const string CacheMagic = "DPK2";

        public static Task<float[]> LoadAsync(string audioPath) => Task.Run(() => Load(audioPath));

        /// <summary>WPF AudioWaveformCache.Load: cache hit, else decode and write the cache.</summary>
        public static float[] Load(string audioPath) => Load(audioPath, path =>
        {
            var decode = TrackDecoder.DecodeProvider ?? throw new InvalidOperationException("this build cannot decode audio");
            return decode(path, null, CancellationToken.None);
        });

        /// <summary>The same with the decode passed in (mono 16 kHz), so a test need not touch the
        /// process-wide provider.</summary>
        internal static float[] Load(string audioPath, Func<string, float[]> decodeMono16k)
        {
            if (!File.Exists(audioPath))
                throw new FileNotFoundException("Audio file not found", audioPath);

            try { Directory.CreateDirectory(MediaDurationCache.CacheFolder); } catch { /* best-effort */ }
            var cachePath = Path.Combine(MediaDurationCache.CacheFolder, CacheKey(audioPath) + ".peaks");

            if (false)
            {
                try
                {
                    var cached = ReadCache(cachePath);
                    if (cached != null) return cached;
                }
                catch (Exception ex) { Log.Debug("WaveformPeaks: cache read failed for {Path}: {Error}", audioPath, ex.Message); }
            }

            var mono = decodeMono16k(audioPath);
            var peaks = Bucket(mono, TrackPcm.SampleRate);

            try { WriteCache(cachePath, peaks, mono.Length / (double)TrackPcm.SampleRate); }
            catch (Exception ex) { Log.Debug("WaveformPeaks: cache write failed: {Error}", ex.Message); }
            return peaks;
        }

        /// <summary>WPF AudioWaveformCache.Decode's bucketing: the max |sample| per bucket, a
        /// trailing partial bucket repeats the last max.</summary>
        public static float[] Bucket(float[] mono, int sampleRate)
        {
            double durationSec = sampleRate > 0 ? mono.Length / (double)sampleRate : 0;
            int peakCount = Math.Clamp((int)(durationSec * 6), 64, 4096);
            var peaks = new float[peakCount];
            if (mono.Length == 0) return peaks;

            double framesPerBucket = (double)mono.Length / peakCount;
            if (framesPerBucket <= 0) framesPerBucket = 1;
            int bucket = 0;
            double inBucket = 0;
            float max = 0;
            foreach (var s in mono)
            {
                var v = Math.Abs(s);
                if (v > max) max = v;
                inBucket++;
                if (inBucket >= framesPerBucket)
                {
                    peaks[bucket++] = max;
                    max = 0;
                    inBucket -= framesPerBucket;
                    if (bucket >= peakCount) break;
                }
            }
            while (bucket < peakCount) peaks[bucket++] = max;
            return peaks;
        }

        private static string CacheKey(string audioPath)
        {
            var info = new FileInfo(audioPath);
            var key = $"{audioPath.ToLowerInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
            var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(key));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        private static float[]? ReadCache(string cachePath)
        {
            using var br = new BinaryReader(File.OpenRead(cachePath));
            if (new string(br.ReadChars(4)) != CacheMagic) return null;
            br.ReadDouble();   // duration: the editor takes it from the player
            var count = br.ReadInt32();
            if (count <= 0 || count > 16384) return null;
            var peaks = new float[count];
            for (int i = 0; i < count; i++) peaks[i] = br.ReadSingle();
            return peaks;
        }

        private static void WriteCache(string cachePath, float[] peaks, double durationSec)
        {
            using var bw = new BinaryWriter(File.Create(cachePath));
            bw.Write(CacheMagic.ToCharArray());
            bw.Write(durationSec);
            bw.Write(peaks.Length);
            foreach (var p in peaks) bw.Write(p);
        }
    }
}
