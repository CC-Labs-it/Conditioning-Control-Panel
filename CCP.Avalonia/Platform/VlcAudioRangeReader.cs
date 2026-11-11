using System;
using System.IO;
using System.Threading;
using ConditioningControlPanel.Avalonia.Views.Games;

namespace ConditioningControlPanel.Avalonia.Platform
{
    // WPF Services/Audio/MediaFoundationRangeReader.cs:17: the analyzer requires 44.1 kHz mono.
    internal static class VlcAudioRangeReader
    {
        internal const int SampleRate = 44100;

        internal static float[] Read(string path, TimeSpan start, TimeSpan end, CancellationToken ct)
            => Read(path, start, end, ct, null);

        internal static float[] Read(string path, TimeSpan start, TimeSpan end, CancellationToken ct, LibVLCSharp.Shared.LibVLC? decoder)
        {
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(path) || start < TimeSpan.Zero || end <= start) return Array.Empty<float>();
            var wav = Path.Combine(Path.GetTempPath(), "ccp-sync-" + Guid.NewGuid().ToString("N") + ".wav");
            try
            {
                var sout = RaceTrackPlayer.SoutFor(wav).Replace("samplerate=16000", "samplerate=44100");
                RaceTrackPlayer.TranscodeToWav(path, wav, null, ct, sout, start, end, decoder);
                ct.ThrowIfCancellationRequested();
                var samples = RaceTrackPlayer.ReadMono16(File.ReadAllBytes(wav), SampleRate);
                ct.ThrowIfCancellationRequested();
                var limit = (int)Math.Min(int.MaxValue, (end - start).TotalSeconds * SampleRate);
                if (samples.Length > limit) Array.Resize(ref samples, limit);
                return samples;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Audio sync range decode failed");
                return Array.Empty<float>();
            }
            finally { try { File.Delete(wav); } catch { } }
        }
    }
}
