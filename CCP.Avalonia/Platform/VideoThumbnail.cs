using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>First video frame for the Library when ffmpeg is available. A missing decoder or
/// unreadable video leaves the existing placeholder, like WPF's failed shell thumbnail.</summary>
internal static class VideoThumbnail
{
    internal static Task<byte[]?> ReadAsync(string path) => ReadAsync(path, "ffmpeg");

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
