using System;
using System.Threading.Tasks;
using LibVLCSharp.Shared;
using NAudio.Wave;

namespace ConditioningControlPanel.Services.Deeper
{
    /// <summary>
    /// The decoding half of <see cref="MediaDurationCache"/>: MediaFoundation (NAudio) and the
    /// head's shared LibVLC instance, neither of which Core can reach. The cache, key and format
    /// rules live in Core.
    /// </summary>
    public static class MediaDurationProbe
    {
        private const int VlcParseTimeoutMs = 5000;

        /// <summary>
        /// Reads the duration of a local file on a worker thread. MediaFoundation
        /// first (header read, no decode, same reader the waveform cache uses),
        /// then a LibVLC parse-only pass for containers MediaFoundation cannot
        /// open. Null when neither source knows.
        /// </summary>
        public static async Task<double?> ProbeAsync(string mediaPath)
        {
            if (!MediaDurationCache.IsProbeable(mediaPath)) return null;
            var viaMf = await Task.Run(() => ProbeMediaFoundation(mediaPath)).ConfigureAwait(false);
            if (viaMf is > 0) return viaMf;
            return await ProbeLibVlcAsync(mediaPath).ConfigureAwait(false);
        }

        private static double? ProbeMediaFoundation(string path)
        {
            try
            {
                using var reader = new MediaFoundationReader(path);
                var seconds = reader.TotalTime.TotalSeconds;
                return seconds > 0 ? seconds : null;
            }
            catch (Exception ex)
            {
                Diag.Swallowed(ex);
                return null;
            }
        }

        private static async Task<double?> ProbeLibVlcAsync(string path)
        {
            try
            {
                var vlc = VideoService.SharedLibVLC;
                if (vlc == null) return null;
                using var media = new Media(vlc, path, FromType.FromPath);
                var status = await media.Parse(MediaParseOptions.ParseLocal, VlcParseTimeoutMs).ConfigureAwait(false);
                if (status != MediaParsedStatus.Done) return null;
                var ms = media.Duration;
                return ms > 0 ? ms / 1000.0 : null;
            }
            catch (Exception ex)
            {
                Diag.Swallowed(ex);
                return null;
            }
        }

    }
}
