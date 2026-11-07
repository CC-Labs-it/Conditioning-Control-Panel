using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using LibVLCSharp.Shared;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF MainWindow.Assets.cs:921-925 uses the shell's video thumbnail. Decode one
/// silent frame with the head's existing VLC instead. Missing codecs leave the placeholder.</summary>
internal static class VideoThumbnail
{
    internal static Task<Bitmap?> LoadAsync(string path) => LoadAsync(path, TimeSpan.FromSeconds(5));

    // Per-call observation only: tests inspect the real player and sink without replacing decoding.
    internal static async Task<Bitmap?> LoadAsync(string path, TimeSpan timeout,
        Action<MediaPlayer, VlcFrameSink>? opened = null)
    {
        var vlc = LibVlcAudio.Shared;
        if (vlc == null) return null;
        using var media = new Media(vlc, path, FromType.FromPath);
        media.AddOption(LibVlcSilence.NoAudioOption);
        using var player = new MediaPlayer(vlc);
        var ready = new TaskCompletionSource<Bitmap?>(TaskCreationOptions.RunContinuationsAsynchronously);
        VlcFrameSink? sink = null;
        sink = new VlcFrameSink(player, () => media, _ => { }, () =>
        {
            if (ready.Task.IsCompleted) return;
            try
            {
                using var png = new MemoryStream();
                sink!.Bitmap!.Save(png);
                png.Position = 0;
                ready.TrySetResult(Bitmap.DecodeToWidth(png, 100));
            }
            catch { ready.TrySetResult(null); }
        }, decodeWidth: 100);
        Bitmap? result = null;
        try
        {
            opened?.Invoke(player, sink);
            if (player.Play(media)) result = await ready.Task.WaitAsync(timeout).ConfigureAwait(false);
        }
        catch { } // Corrupt/unsupported media: WPF leaves its placeholder too.
        finally
        {
            // Stop joins the decoder before freeing its callback buffer. Never block the UI.
            await Task.Run(player.Stop).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ready.TrySetResult(null); // queued blits must not create an unowned bitmap after timeout
                sink.Free();
            });
        }
        // A frame can race the timeout; dispose it if the caller will never receive it.
        if (result == null && ready.Task.IsCompletedSuccessfully) ready.Task.Result?.Dispose();
        return result;
    }
}
