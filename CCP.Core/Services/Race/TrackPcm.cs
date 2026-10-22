using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace ConditioningControlPanel.Services.Race;

/// <summary>A decoded track: mono 16 kHz float PCM plus what the chart needs to name and key it.</summary>
public sealed class TrackPcm
{
    public const int SampleRate = 16000;

    public float[] Mono16k { get; init; } = Array.Empty<float>();
    public double DurationSec { get; init; }
    /// <summary>SHA1 hex of the file length (8 bytes LE) + the first 1 MiB. See CHART.md.</summary>
    public string Hash { get; init; } = "";
    /// <summary>The file name without its directory.</summary>
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
}

/// <summary>
/// Decodes an audio file to <see cref="TrackPcm"/>. The decode itself is the head's
/// (<see cref="DecodeProvider"/>): WPF seeds its NAudio reader (Services/Race/NAudioTrackDecode.cs).
/// </summary>
public static class TrackDecoder
{
    /// <summary>(path, progress, cancel) -> mono 16 kHz float PCM. Seeded by the head; unseeded means
    /// this build has no decoder, and Decode says so.</summary>
    public static volatile Func<string, IProgress<double>?, CancellationToken, float[]>? DecodeProvider;

    /// <summary>
    /// Decodes <paramref name="path"/> to mono 16 kHz float PCM. Progress is bytes decoded over the
    /// reader's length, 0..1. Cancellation is checked between blocks and throws
    /// <see cref="OperationCanceledException"/>.
    /// </summary>
    public static TrackPcm Decode(string path, IProgress<double>? progress, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException("Track file not found", path ?? "");

        var decode = DecodeProvider ?? throw new InvalidOperationException("this build cannot decode audio");
        var pcm = decode(path, progress, ct);
        return new TrackPcm
        {
            Mono16k = pcm,
            DurationSec = pcm.Length / (double)TrackPcm.SampleRate,
            Hash = HashFile(path),
            Name = System.IO.Path.GetFileName(path),
            Path = path
        };
    }

    /// <summary>CHART.md: the hash reads the first 1 MiB and nothing else.</summary>
    public const int HashHead = 1024 * 1024;

    /// <summary>The cache key: SHA1 of the length prefix + the first 1 MiB, so a rename keeps its chart.</summary>
    public static string HashFile(string path)
    {
        long length = new FileInfo(path).Length;
        var head = new byte[HashHead];
        int filled = 0;
        using (var file = File.OpenRead(path))
        {
            int got;
            while (filled < head.Length && (got = file.Read(head, filled, head.Length - filled)) > 0)
                filled += got;
        }
        return HashBytes(length, filled == head.Length ? head : head[..filled]);
    }

    /// <summary>
    /// The same number as <see cref="HashFile"/> from a length and the head bytes on their own, for
    /// the caller that has those without having the file: the cloud path asks the CDN for a length
    /// and one megabyte so an authored or already charted track costs no download. Pass at most the
    /// first <see cref="HashHead"/> bytes, and fewer when the file is shorter than that.
    /// </summary>
    public static string HashBytes(long length, byte[] head)
    {
        if (head == null) throw new ArgumentNullException(nameof(head));
        int count = Math.Min(head.Length, HashHead);

        var prefix = BitConverter.GetBytes(length);
        if (!BitConverter.IsLittleEndian) Array.Reverse(prefix);

        using var sha = SHA1.Create();
        sha.TransformBlock(prefix, 0, prefix.Length, null, 0);
        sha.TransformFinalBlock(head, 0, count);
        return Convert.ToHexString(sha.Hash ?? Array.Empty<byte>()).ToLowerInvariant();
    }
}
