// Adapted from parity/trunk 447dfd0b10827ae3588c31c7ef53dd20c89adc10; reuse this head's VlcFrameSink.
using System;
using System.IO;
using Avalonia.Media.Imaging;
using ConditioningControlPanel.Avalonia.Platform;
using LibVLCSharp.Shared;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

internal static partial class SpiralOverlay
{
    private static IDisposable? _videoPlayer;
    internal static bool IsVideoPlaying => _videoPlayer != null;
    internal static int VideoFrames => (_videoPlayer as VideoPlayer)?.Frames ?? 0;

    // WPF CreateSpiralVideoWindow: muted, UniformToFill, MediaEnded -> restart. One decoder feeds all screens.
    internal static bool IsVideo(string path) => Path.GetExtension(path).ToLowerInvariant()
        is ".mp4" or ".webm" or ".mov" or ".avi" or ".mkv";

    // Headless tests replace the native player; all window, source-change and panic paths stay real.
    internal static Func<string, Action<Bitmap>, IDisposable?> VideoStart = (path, show) =>
        LibVlcAudio.Shared is { } vlc ? new VideoPlayer(vlc, path, show) : null;

    private static void ShowVideoFrame(Bitmap frame)
    {
        foreach (var window in Windows)
        {
            window.Spiral.Source = frame;
            window.Spiral.InvalidateVisual();
        }
    }

    private static void StopVideo()
    {
        var player = _videoPlayer;
        _videoPlayer = null;
        // Drop the UI references before freeing the sink's bitmap.
        foreach (var window in Windows) window.Spiral.Source = null;
        try { player?.Dispose(); }
        catch (Exception ex) { Log.Debug(ex, "Spiral: video stop failed"); }
    }

    private sealed class VideoPlayer : IDisposable
    {
        private readonly MediaPlayer _player;
        private readonly Media _media;
        private readonly VlcFrameSink _sink;
        internal int Frames => _sink.FrameCount;

        internal VideoPlayer(LibVLC vlc, string path, Action<Bitmap> show)
        {
            _player = new MediaPlayer(vlc) { EnableHardwareDecoding = true };
            _media = new Media(vlc, path, FromType.FromPath);
            _sink = new VlcFrameSink(_player, () => _media, show, () =>
            {
                if (_sink!.Bitmap is { } bitmap) show(bitmap);
            });
            // Per-media no-audio avoids changing the shared player's/process's audio state.
            _media.AddOption(":no-audio");
            _media.AddOption(":input-repeat=65535");
            try
            {
                if (!_player.Play(_media)) throw new InvalidOperationException("Video spiral playback refused");
            }
            catch { Dispose(); throw; }
        }

        public void Dispose()
        {
            try { _player.Stop(); } // joins decoder callbacks before freeing the buffer
            finally { _player.Dispose(); _media.Dispose(); _sink.Free(); }
        }
    }
}
