using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;

namespace ConditioningControlPanel.Avalonia.Platform;

internal static class BrowserAudioRangeReader
{
    public static float[] Read(string path, TimeSpan start, TimeSpan end, CancellationToken ct)
    {
        if (OperatingSystem.IsWindows()) return MediaFoundationRangeReader.Read(path, start, end, ct);
        // ponytail: use the distro's ffmpeg decoder; a missing decoder unblocks video without haptics.
        var info = new ProcessStartInfo("ffmpeg") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in new[] { "-v", "error", "-ss", start.TotalSeconds.ToString(CultureInfo.InvariantCulture), "-i", path,
                     "-t", (end - start).TotalSeconds.ToString(CultureInfo.InvariantCulture), "-vn", "-ac", "1", "-ar", "44100", "-f", "f32le", "pipe:1" })
            info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new IOException("Audio decoder unavailable");
        using var cancel = ct.Register(() => { try { process.Kill(true); } catch (InvalidOperationException) { } });
        var errors = process.StandardError.ReadToEndAsync(ct);
        using var bytes = new MemoryStream();
        process.StandardOutput.BaseStream.CopyToAsync(bytes, ct).GetAwaiter().GetResult();
        process.WaitForExit();
        ct.ThrowIfCancellationRequested();
        if (process.ExitCode != 0) throw new IOException("Audio decoder failed");
        errors.GetAwaiter().GetResult();
        var samples = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes.ToArray(), 0, samples, 0, samples.Length * 4);
        return samples;
    }
}
