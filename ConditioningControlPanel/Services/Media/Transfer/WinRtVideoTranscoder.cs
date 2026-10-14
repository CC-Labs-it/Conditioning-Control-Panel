using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Serilog;

namespace ConditioningControlPanel.Services.Transfer
{
    /// <summary>
    /// Lane C's engine on Windows, WinRT <see cref="MediaTranscoder"/> (disk → disk, hardware
    /// accelerated, real Progress, real Cancel). Measured ~30 MB/s on the spike box: a 90 MB 1080p
    /// clip became 18 MB of 720p in 3.1 s.
    ///
    /// Also mints the 426×240 micro-preview via a second pass with TrimStartTime/TrimStopTime and
    /// <c>profile.Audio = null</c> (2 s ≈ 96 KB).
    ///
    /// Everything here is off-UI-thread by construction; nothing touches the Dispatcher. The targets
    /// and the seams this fills are <see cref="VideoTranscodeLane"/> in CCP.Core.
    /// </summary>
    internal static class WinRtVideoTranscoder
    {
        /// <summary>Seed <see cref="VideoTranscodeLane"/>'s three seams with this engine.</summary>
        public static void Install()
        {
            VideoTranscodeLane.Probe = ProbeAsync;
            VideoTranscodeLane.Transcode = TranscodeAsync;
            VideoTranscodeLane.Preview = MakePreviewAsync;
        }

        /// <summary>
        /// Width/height/duration/bitrate straight off the Windows property handler. Null when the
        /// shell can't describe the file — callers must treat that as "probe unknown", never as
        /// "unsupported": the transcoder is the only thing allowed to say no.
        /// </summary>
        private static async Task<VideoTranscodeLane.VideoProbe?> ProbeAsync(string path, CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                var file = await StorageFile.GetFileFromPathAsync(path).AsTask(ct).ConfigureAwait(false);
                var props = await file.Properties.GetVideoPropertiesAsync().AsTask(ct).ConfigureAwait(false);
                int w = (int)props.Width, h = (int)props.Height;
                int durMs = (int)Math.Min(int.MaxValue, props.Duration.TotalMilliseconds);
                long bitrate = props.Bitrate;
                // A zero-everything probe is the shell shrugging, not a real answer.
                if (w <= 0 && h <= 0 && durMs <= 0 && bitrate <= 0) return null;
                return new VideoTranscodeLane.VideoProbe(w, h, durMs, bitrate);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Log.Debug("WinRtVideoTranscoder.ProbeAsync({Path}): {E}", path, ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Transcode <paramref name="srcPath"/> to H.264/AAC MP4 at <paramref name="tmpOut"/>.
        /// <paramref name="onProgress"/> receives 0-100 on the transcoder's own thread.
        /// Throws <see cref="TranscodeUnsupportedException"/> when Windows can't read the source.
        /// </summary>
        private static async Task<VideoTranscodeLane.TranscodeResult> TranscodeAsync(
            string srcPath, string tmpOut, VideoTranscodeLane.VideoProbe? probe,
            Action<double>? onProgress, CancellationToken ct)
        {
            var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p);
            profile.Video.Bitrate = (uint)VideoTranscodeLane.TargetBitrateFor(probe?.Bitrate ?? 0);
            if (probe is { Width: > 0, Height: > 0 } p)
            {
                // Fit inside 1280x720 on the long side, even dimensions (H.264 chroma), and NEVER
                // upscale: the profile's own 1280x720 default would blow a 640x480 clip up.
                var (w, h) = VideoTranscodeLane.FitEven(p.Width, p.Height, VideoTranscodeLane.MaxWidth, VideoTranscodeLane.MaxHeight);
                profile.Video.Width = w;
                profile.Video.Height = h;
            }

            await RunAsync(srcPath, tmpOut, profile, null, null, onProgress, ct).ConfigureAwait(false);

            long bytes = new FileInfo(tmpOut).Length;
            return new VideoTranscodeLane.TranscodeResult(tmpOut, "mp4", "avc1",
                (int)profile.Video.Width, (int)profile.Video.Height, probe?.DurMs ?? 0, bytes);
        }

        /// <summary>
        /// 2-second, 426×240, audio-free micro-preview starting 12% into the clip (clamped so the
        /// window always fits inside the duration). Used by the assets grid and, later, by the
        /// offer card — never by playback.
        /// </summary>
        private static async Task<long> MakePreviewAsync(
            string srcPath, string tmpOut, VideoTranscodeLane.VideoProbe? probe, CancellationToken ct)
        {
            var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Vga);
            profile.Audio = null;                       // a preview with sound is a jump-scare
            profile.Video.Bitrate = (uint)VideoTranscodeLane.PreviewBitrate;
            var (w, h) = probe is { Width: > 0, Height: > 0 } p
                ? VideoTranscodeLane.FitEven(p.Width, p.Height, VideoTranscodeLane.PreviewWidth, VideoTranscodeLane.PreviewHeight)
                : ((uint)VideoTranscodeLane.PreviewWidth, (uint)VideoTranscodeLane.PreviewHeight);
            profile.Video.Width = w;
            profile.Video.Height = h;
            try
            {
                if (profile.Video.FrameRate != null)
                {
                    profile.Video.FrameRate.Numerator = VideoTranscodeLane.PreviewFps;
                    profile.Video.FrameRate.Denominator = 1;
                }
            }
            catch { /* some profiles refuse a framerate override; 30fps of 2s is survivable */ }

            var (startMs, stopMs) = VideoTranscodeLane.PreviewWindow(probe?.DurMs ?? 0);
            var start = TimeSpan.FromMilliseconds(startMs);
            var stop = TimeSpan.FromMilliseconds(stopMs);

            await RunAsync(srcPath, tmpOut, profile, start, stop, null, ct).ConfigureAwait(false);
            return new FileInfo(tmpOut).Length;
        }

        private static async Task RunAsync(
            string srcPath, string tmpOut, MediaEncodingProfile profile,
            TimeSpan? trimStart, TimeSpan? trimStop,
            Action<double>? onProgress, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(tmpOut)!);

            StorageFile src, dst;
            try
            {
                src = await StorageFile.GetFileFromPathAsync(srcPath).AsTask(ct).ConfigureAwait(false);
                var dstDir = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(tmpOut)!)
                    .AsTask(ct).ConfigureAwait(false);
                dst = await dstDir.CreateFileAsync(Path.GetFileName(tmpOut), CreationCollisionOption.ReplaceExisting)
                    .AsTask(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw new TranscodeUnsupportedException("io: " + ex.Message);
            }

            var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };
            if (trimStart.HasValue) transcoder.TrimStartTime = trimStart.Value;
            if (trimStop.HasValue) transcoder.TrimStopTime = trimStop.Value;

            var prep = await transcoder.PrepareFileTranscodeAsync(src, dst, profile).AsTask(ct).ConfigureAwait(false);
            if (!prep.CanTranscode)
            {
                TryDelete(tmpOut);
                Log.Information("WinRtVideoTranscoder: cannot transcode {Path} ({Reason})",
                    srcPath, prep.FailureReason);
                throw new TranscodeUnsupportedException(TransferFailReasons.NoDecoder);
            }

            var op = prep.TranscodeAsync();
            if (onProgress != null)
            {
                op.Progress = (_, percent) =>
                {
                    try { onProgress(percent); }
                    catch { /* a UI subscriber must never kill an encode */ }
                };
            }
            using var reg = ct.Register(() => { try { op.Cancel(); } catch { } });
            try
            {
                await op.AsTask().ConfigureAwait(false);
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                TryDelete(tmpOut);
                throw new OperationCanceledException(ct);
            }
            catch (Exception ex)
            {
                TryDelete(tmpOut);
                Log.Warning("WinRtVideoTranscoder: transcode of {Path} failed: {E}", srcPath, ex.Message);
                throw;
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
