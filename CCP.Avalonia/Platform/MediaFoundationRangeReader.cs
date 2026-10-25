using System;
using System.Collections.Generic;
using System.Threading;
using NAudio.Wave;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// The WPF head's audio decoder for <see cref="ChunkManager"/> (Core): NAudio's MediaFoundationReader,
    /// which is Windows-only, so it stays here and is handed to ChunkManager by AudioSyncService.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal static class MediaFoundationRangeReader
    {
        /// <summary>
        /// Extract audio samples for a specific time range
        /// </summary>
        public static float[] Read(string videoPath, TimeSpan startTime, TimeSpan endTime, CancellationToken ct)
        {
            try
            {
                using var reader = new MediaFoundationReader(videoPath);

                // Seek FIRST before creating sample provider
                if (startTime > TimeSpan.Zero)
                {
                    reader.CurrentTime = startTime;
                    Log.Debug("ChunkManager: Seeked to {Time:F1}s", startTime.TotalSeconds);
                }

                // Now create sample provider from seeked position
                var sampleProvider = reader.ToSampleProvider();

                // Convert to mono if stereo
                if (sampleProvider.WaveFormat.Channels > 1)
                {
                    sampleProvider = sampleProvider.ToMono();
                }

                var sampleRate = sampleProvider.WaveFormat.SampleRate;
                var durationSeconds = (endTime - startTime).TotalSeconds;
                var samplesToRead = (int)(durationSeconds * sampleRate);

                // Read samples for this chunk only
                var samples = new List<float>(samplesToRead);
                var buffer = new float[4096];
                int totalRead = 0;

                while (totalRead < samplesToRead)
                {
                    ct.ThrowIfCancellationRequested();

                    var toRead = Math.Min(buffer.Length, samplesToRead - totalRead);
                    var read = sampleProvider.Read(buffer, 0, toRead);

                    if (read == 0)
                        break; // End of file

                    for (int i = 0; i < read; i++)
                    {
                        samples.Add(buffer[i]);
                    }
                    totalRead += read;
                }

                Log.Information("ChunkManager: Extracted {Count} samples for range {Start:F1}s - {End:F1}s",
                    samples.Count, startTime.TotalSeconds, endTime.TotalSeconds);

                return samples.ToArray();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ChunkManager: Failed to extract audio for range {Start:F1}s - {End:F1}s",
                    startTime.TotalSeconds, endTime.TotalSeconds);
                return Array.Empty<float>();
            }
        }
    }
}
