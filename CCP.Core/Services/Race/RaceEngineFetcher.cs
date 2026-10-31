using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.Race;

public readonly record struct RaceEngineProgress(long Received, long? Total);

/// <summary>Verified, cancellable cache transaction. No process is started here.</summary>
public sealed class RaceEngineFetcher(HttpClient http, string userData)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public static string DefaultRoot => Path.Combine(CorePaths.UserData, "engines");
    private readonly string root = Path.GetFullPath(Path.Combine(userData, "engines"));

    public async Task<string> FetchAsync(RaceEnginePackage package,
        IProgress<RaceEngineProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        // Validate before touching disk, including injected packages used by tests.
        SafeName(package.Version);
        SafeName(package.Architecture);
        _ = Convert.FromHexString(package.Sha256.Length == 64 ? package.Sha256 : throw new InvalidDataException("Invalid SHA256."));
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? staging = null;
        try
        {
            NoLinks(root);
            Directory.CreateDirectory(root);
            var target = Path.Combine(root, package.DirectoryName);
            NoLinks(target);
            var executable = Path.Combine(target, "electron");
            var receipt = Path.Combine(target, ".verified-sha256");
            NoLinks(executable);
            NoLinks(receipt);
            if (File.Exists(executable) && File.Exists(receipt) && File.ReadAllText(receipt) == package.Sha256)
                return executable;
            staging = Path.Combine(root, ".fetch-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            var zip = Path.Combine(staging, "engine.zip.part");
            using (var response = await http.GetAsync(package.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var output = File.Create(zip);
                var buffer = new byte[81920];
                long received = 0;
                int count;
                while ((count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                    received += count;
                    progress?.Report(new(received, response.Content.Headers.ContentLength));
                }
                if (response.Content.Headers.ContentLength is long total && received != total)
                    throw new InvalidDataException("Incomplete race engine download.");
            }
            await using (var stream = File.OpenRead(zip))
            {
                var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
                if (!CryptographicOperations.FixedTimeEquals(hash, Convert.FromHexString(package.Sha256)))
                    throw new InvalidDataException("Race engine checksum mismatch.");
            }
            var unpack = Path.Combine(staging, "unpacked");
            Directory.CreateDirectory(unpack);
            using (var archive = ZipFile.OpenRead(zip))
            {
                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var name = entry.FullName;
                    var mode = (entry.ExternalAttributes >> 16) & 0xffff;
                    if (name.Contains('\\') || name.Contains(':') || Path.IsPathRooted(name) || (mode & 0xf000) == 0xa000)
                        throw new InvalidDataException("Unsafe race engine archive entry.");
                    var destination = Path.GetFullPath(Path.Combine(unpack, name));
                    if (!destination.StartsWith(unpack + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                        throw new InvalidDataException("Race engine archive escapes cache.");
                    if (name.EndsWith('/')) { Directory.CreateDirectory(destination); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    await using (var input = entry.Open())
                    await using (var output = new FileStream(destination, FileMode.CreateNew))
                        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                    if (!OperatingSystem.IsWindows())
                    {
                        var execute = (UnixFileMode)(mode & 0x49); // only execute bits, never setuid
                        File.SetUnixFileMode(destination, UnixFileMode.UserRead | UnixFileMode.UserWrite |
                            UnixFileMode.GroupRead | UnixFileMode.OtherRead | execute);
                    }
                }
            }
            if (!File.Exists(Path.Combine(unpack, "electron"))) throw new InvalidDataException("Race engine executable missing.");
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(Path.Combine(unpack, "electron"), UnixFileMode.UserRead | UnixFileMode.UserWrite |
                    UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            File.WriteAllText(Path.Combine(unpack, ".verified-sha256"), package.Sha256);
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(target)) Directory.Delete(target, true);
            Directory.Move(unpack, target);
            // Only our versioned engine directories; unrelated cache data is never swept.
            foreach (var old in Directory.GetDirectories(root, "electron-*"))
            {
                if (old == target) continue;
                NoLinks(old);
                Directory.Delete(old, true);
            }
            return executable;
        }
        finally
        {
            try { if (staging != null) Directory.Delete(staging, true); }
            finally { Gate.Release(); }
        }
    }

    private static void SafeName(string value)
    {
        if (value.Length == 0 || value is "." or ".." || !System.Linq.Enumerable.All(value,
            c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-')) throw new InvalidDataException("Invalid engine pin.");
    }

    private static void NoLinks(string path)
    {
        for (var current = new DirectoryInfo(path); current != null; current = current.Parent)
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0 && current.LinkTarget != null)
                throw new IOException("Race engine cache contains a link.");
    }
}
