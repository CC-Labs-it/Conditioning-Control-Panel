using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using LibVLCSharp.Shared;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>First video frame via the shared LibVLC; ffmpeg is a fallback if it is unavailable.
/// An unreadable video leaves the placeholder, like WPF's failed shell thumbnail.</summary>
internal static class VideoThumbnail
{
    // WPF ShellThumbnailHelper.cs:73-98 asks the platform decoder for a 100px thumbnail.
    // Windows bundles LibVLC; Linux uses the same system library as the video player.
    internal static async Task<byte[]?> ReadAsync(string path)
    {
        if (LibVlcAudio.Shared is { } vlc)
        {
            var bytes = await ReadVlcAsync(path, vlc).ConfigureAwait(false);
            if (bytes != null) return bytes;
        }
        return await ReadAsync(path, "ffmpeg").ConfigureAwait(false);
    }

    private static async Task<byte[]?> ReadVlcAsync(string path, LibVLC vlc)
    {
        VlcFrameSink? sink = null;
        try
        {
            using var media = new Media(vlc, path, FromType.FromPath);
            using var player = new MediaPlayer(vlc);
            media.AddOption(LibVlcSilence.NoAudioOption);
            var frame = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
            player.EncounteredError += (_, _) => frame.TrySetResult(null);
            // Let already queued render-priority blits deliver a last frame before ending.
            player.EndReached += (_, _) => Dispatcher.UIThread.Post(() => frame.TrySetResult(null), DispatcherPriority.Background);
            sink = new VlcFrameSink(player, () => media, _ => { }, () =>
            {
                try
                {
                    if (sink?.Bitmap is not { } bitmap) return;
                    using var output = new MemoryStream();
                    bitmap.Save(output);
                    frame.TrySetResult(output.ToArray());
                }
                catch { frame.TrySetResult(null); }
            }, maxDimension: 100);
            try
            {
                if (!player.Play(media)) return null;
                return await frame.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            }
            finally
            {
                try { player.Stop(); } catch { }
                // Stop joins decoder callbacks; queued UI blits must finish before bitmap disposal.
                await FreeOnUiAsync(sink.Free).ConfigureAwait(false);
            }
        }
        catch { return null; }
    }

    internal static async Task FreeOnUiAsync(Action free) => await Dispatcher.UIThread.InvokeAsync(free);

    // Executable is injectable for exercising the process boundary without an installed decoder.
    internal static async Task<byte[]?> ReadAsync(string path, string executable)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = executable, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var arg in new[] { "-nostdin", "-v", "error", "-i", path, "-frames:v", "1",
                     "-vf", "scale=100:100:force_original_aspect_ratio=decrease",
                     "-f", "image2pipe", "-vcodec", "png", "pipe:1" })
            process.StartInfo.ArgumentList.Add(arg);

        var started = false;
        try
        {
            started = process.Start();
            if (!started) return null;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var output = new MemoryStream();
            var errors = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.StandardOutput.BaseStream.CopyToAsync(output, timeout.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            await errors.ConfigureAwait(false);
            return process.ExitCode == 0 && output.Length > 0 ? output.ToArray() : null;
        }
        catch
        {
            // Wait for a timed-out decoder to release the decrypted file before the caller deletes it.
            if (started)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
                try { await process.WaitForExitAsync().ConfigureAwait(false); } catch { }
            }
            return null;
        }
    }
}
