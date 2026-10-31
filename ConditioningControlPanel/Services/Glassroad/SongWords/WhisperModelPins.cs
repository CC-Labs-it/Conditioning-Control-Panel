using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.Glassroad.SongWords;

/// <summary>One pinned whisper.cpp model: its download, exact size and sha256. The file lands under the user
/// data folder on first use; nothing ships in the installer.</summary>
public sealed record WhisperModelPin(string Id, string FileName, string Url, long Bytes, string Sha256)
{
    /// <summary>"1.4 GB" / "465 MB", invariant (the page shows it; never the machine's decimal comma).</summary>
    public string SizeLabel => Bytes >= 1L << 30
        ? (Bytes / (double)(1L << 30)).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " GB"
        : (Bytes / (double)(1L << 20)).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " MB";
}

/// <summary>
/// The listening models for "race your own song" (Racing Thoughts 3D, solo): whisper.cpp ggml files from the
/// upstream Hugging Face mirror, pinned by size and sha256. Multilingual, so a song in any of the app's languages
/// gets its words; the model is picked by the machine (cores and memory), never by the player, so the wait stays
/// inside the promise: pick a song, wait a minute or two, race. Measured 2026-10-31 on 8 threads: small runs
/// about 11x real time, medium about 3.5x; a 3-minute song is 20 s or 1 minute here, two to three times that
/// on a four-core laptop.
/// </summary>
public static class WhisperModelPins
{
    private const string Mirror = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/";

    /// <summary>Small (multilingual): the fallback for machines under <see cref="MediumCores"/> cores or 8 GB.</summary>
    public static readonly WhisperModelPin Small = new("small", "ggml-small.bin", Mirror + "ggml-small.bin",
        487601967, "1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b");

    /// <summary>Medium (multilingual): the default on a desktop-class CPU.</summary>
    public static readonly WhisperModelPin Medium = new("medium", "ggml-medium.bin", Mirror + "ggml-medium.bin",
        1533763059, "6c14d5adee5f86394037b4e4e8b59f1673b6cee10e3cf0b11bbdbee79c156208");

    /// <summary>Logical cores from which the medium model keeps a 3-minute song under about two minutes.</summary>
    public const int MediumCores = 12;
    public const long MediumRamBytes = 8L << 30;

    /// <summary>The model this machine can afford inside the wait budget.</summary>
    public static WhisperModelPin Choose(int logicalCores, long totalRamBytes)
        => logicalCores >= MediumCores && totalRamBytes >= MediumRamBytes ? Medium : Small;

    /// <summary>Whisper threads: half the logical cores, two to eight (the app and the race keep the rest).</summary>
    public static int Threads(int logicalCores) => Math.Clamp(logicalCores / 2, 2, 8);

    public static string Folder(string userDataPath) => Path.Combine(userDataPath, "models", "whisper");
    public static string PathFor(string folder, WhisperModelPin pin) => Path.Combine(folder, pin.FileName);

    /// <summary>Installed = the file is there at its pinned size (the sha256 is checked once, on download).</summary>
    public static bool IsInstalled(string folder, WhisperModelPin pin)
    {
        try { var f = new FileInfo(PathFor(folder, pin)); return f.Exists && f.Length == pin.Bytes; }
        catch (Exception) { return false; }
    }
}

public enum WhisperInstallResult { Installed, Cancelled, Network, Disk, SizeMismatch, HashMismatch }

/// <summary>Downloads one pinned model to <c>&lt;file&gt;.part</c>, checks size and sha256, renames it in.
/// A failed or cancelled run leaves nothing behind.</summary>
public sealed class WhisperModelInstaller
{
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly string _folder;
    private readonly WhisperModelPin _pin;
    private readonly Func<CancellationToken, Task<Stream>> _open;

    public WhisperModelInstaller(string folder, WhisperModelPin pin, Func<CancellationToken, Task<Stream>>? open = null)
    {
        _folder = folder; _pin = pin;
        _open = open ?? (ct => OpenHttp(pin.Url, ct));
    }

    public string Target => WhisperModelPins.PathFor(_folder, _pin);
    public string PartFile => Target + ".part";

    public async Task<WhisperInstallResult> InstallAsync(IProgress<double>? progress, CancellationToken ct)
    {
        if (WhisperModelPins.IsInstalled(_folder, _pin)) { progress?.Report(1); return WhisperInstallResult.Installed; }
        try
        {
            Directory.CreateDirectory(_folder);
            TryDelete(PartFile);
            using var sha = SHA256.Create();
            long total = 0;
            await using (var src = await _open(ct).ConfigureAwait(false))
            await using (var dst = new FileStream(PartFile, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            {
                var buf = new byte[1 << 16];
                int n;
                while ((n = await src.ReadAsync(buf.AsMemory(0, buf.Length), ct).ConfigureAwait(false)) > 0)
                {
                    total += n;
                    if (total > _pin.Bytes) { dst.Close(); TryDelete(PartFile); return WhisperInstallResult.SizeMismatch; }
                    sha.TransformBlock(buf, 0, n, null, 0);
                    await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                    progress?.Report(0.99 * total / _pin.Bytes);
                }
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            if (total != _pin.Bytes) { TryDelete(PartFile); return WhisperInstallResult.SizeMismatch; }
            if (!string.Equals(Convert.ToHexString(sha.Hash!), _pin.Sha256, StringComparison.OrdinalIgnoreCase))
            { TryDelete(PartFile); return WhisperInstallResult.HashMismatch; }
            TryDelete(Target);
            File.Move(PartFile, Target);
            progress?.Report(1);
            return WhisperInstallResult.Installed;
        }
        catch (OperationCanceledException) { TryDelete(PartFile); return WhisperInstallResult.Cancelled; }
        catch (HttpRequestException) { TryDelete(PartFile); return WhisperInstallResult.Network; }
        catch (IOException) { TryDelete(PartFile); return WhisperInstallResult.Disk; }
        catch (UnauthorizedAccessException) { TryDelete(PartFile); return WhisperInstallResult.Disk; }
    }

    private static async Task<Stream> OpenHttp(string url, CancellationToken ct)
    {
        var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (Exception) { /* a locked leftover is swept next time */ }
    }
}
