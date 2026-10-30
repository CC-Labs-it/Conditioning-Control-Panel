using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services.Companion
{
    /// <summary>The one pinned download of Rain's big files (lane R12, RAIN-V2-PLAN owner answer 2).</summary>
    public sealed record RainAssetPin(string Url, string Sha256, long Bytes, string Version)
    {
        /// <summary>True only for a real pin: https url, 64 hex sha256, a size and a version. A blank or
        /// placeholder pin is never downloaded; the card says "not available yet" instead.</summary>
        public bool IsReady =>
            Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && Bytes > 0 && !string.IsNullOrWhiteSpace(Version)
            && Sha256.Length == 64 && Sha256.All(Uri.IsHexDigit);
    }

    public static class RainAssetPins
    {
        // ===== UPDATE HERE when rain-assets.zip is uploaded to the release tag (sha256 of the zip, exact byte size). =====
        public const string Placeholder = "PLACEHOLDER-UPLOAD-THE-ZIP-THEN-PUT-ITS-SHA256-HERE";
        public static readonly RainAssetPin Current = new(
            Url: "https://github.com/CC-Labs-it/Conditioning-Control-Panel/releases/download/rain-assets-v1/rain-assets.zip",
            Sha256: "60b1c6b9cc73665dc827ff59c78466ea305a490d3a3de72ecac528636d0c70f8",
            Bytes: 248_741_064,
            Version: "rain-v2-17-20261030");
        // ===== end of the pin =====
    }

    public enum RainAssetState { Installed, Missing, NotAvailable }

    public enum RainInstallResult { Installed, NotAvailable, Cancelled, Network, SizeMismatch, HashMismatch, BadArchive, Disk }

    /// <summary>
    /// Downloads the pinned zip, hashes it while streaming, refuses a size or sha256 mismatch, unpacks into
    /// <c>&lt;dir&gt;.tmp</c> (zip-slip guarded), checks the required files, writes <c>installed.json</c> and
    /// renames the folder into place, so a half folder is never visible. <c>CCP_RAIN_ASSETS_DIR</c> wins:
    /// that folder is reported installed and never touched.
    /// </summary>
    public sealed class RainAssetInstaller
    {
        public const string Marker = "installed.json";
        private static readonly HttpClient Http = CreateHttp();

        private readonly string _dir;
        private readonly RainAssetPin _pin;
        private readonly bool _devOverride;
        private readonly Func<CancellationToken, Task<Stream>> _open;

        public RainAssetInstaller(string dir, RainAssetPin pin, bool devOverride, Func<CancellationToken, Task<Stream>>? open = null)
        {
            _dir = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            _pin = pin;
            _devOverride = devOverride;
            _open = open ?? (ct => OpenHttp(pin.Url, ct));
        }

        /// <summary>The live app's installer: the pinned pack into <see cref="RainWardrobeAssets"/>' folder.</summary>
        public static RainAssetInstaller ForApp()
        {
            var env = Environment.GetEnvironmentVariable(RainWardrobeAssets.EnvVar);
            return new RainAssetInstaller(RainWardrobeAssets.ResolveDir(env, App.UserDataPath), RainAssetPins.Current,
                !string.IsNullOrWhiteSpace(env));
        }

        public string TempDir => _dir + ".tmp";
        private string DownloadFile => _dir + ".download";
        private string OldDir => _dir + ".old";

        public bool IsInstalled() => _devOverride || RainWardrobeAssets.Missing(_dir).Count == 0;

        public RainAssetState State => IsInstalled() ? RainAssetState.Installed
            : _pin.IsReady ? RainAssetState.Missing : RainAssetState.NotAvailable;

        /// <summary>Removes what an interrupted run left beside the folder (never the folder itself).</summary>
        public void SweepLeftovers()
        {
            if (!_devOverride) { TryDeleteDir(TempDir); TryDeleteDir(OldDir); TryDeleteFile(DownloadFile); }
        }

        /// <summary>Progress reports 0..1 of the pinned size. Never throws; cancellation = <see cref="RainInstallResult.Cancelled"/>.</summary>
        public async Task<RainInstallResult> InstallAsync(IProgress<double>? progress, CancellationToken ct)
        {
            if (_devOverride) return RainInstallResult.Installed;
            SweepLeftovers();
            if (IsInstalled()) return RainInstallResult.Installed;
            if (!_pin.IsReady) return RainInstallResult.NotAvailable;
            var downloading = true;
            try
            {
                var fetched = await DownloadAsync(progress, ct).ConfigureAwait(false);
                if (fetched != RainInstallResult.Installed) return fetched;
                downloading = false;
                ct.ThrowIfCancellationRequested();
                if (!Extract(DownloadFile, TempDir)) { SweepLeftovers(); return RainInstallResult.BadArchive; }
                TryDeleteFile(DownloadFile);
                if (RainWardrobeAssets.Missing(TempDir).Count > 0) { SweepLeftovers(); return RainInstallResult.BadArchive; }
                File.WriteAllText(Path.Combine(TempDir, Marker), JsonConvert.SerializeObject(new { version = _pin.Version, sha256 = _pin.Sha256.ToLowerInvariant() }));
                ct.ThrowIfCancellationRequested();
                // The swap: an old partial folder steps aside, the checked one is renamed in, the old one goes.
                if (Directory.Exists(_dir)) Directory.Move(_dir, OldDir);
                Directory.Move(TempDir, _dir);
                TryDeleteDir(OldDir);
                progress?.Report(1);
                return RainInstallResult.Installed;
            }
            catch (OperationCanceledException) { SweepLeftovers(); return RainInstallResult.Cancelled; }
            catch (HttpRequestException) { SweepLeftovers(); return RainInstallResult.Network; }
            catch (IOException) { SweepLeftovers(); return downloading ? RainInstallResult.Network : RainInstallResult.Disk; }
            catch (UnauthorizedAccessException) { SweepLeftovers(); return RainInstallResult.Disk; }
            catch (Exception ex) { App.Logger?.Warning("RainAssets: install failed: {E}", ex.Message); SweepLeftovers(); return RainInstallResult.Network; }
        }

        private async Task<RainInstallResult> DownloadAsync(IProgress<double>? progress, CancellationToken ct)
        {
            using var sha = SHA256.Create();
            long total = 0;
            await using (var src = await _open(ct).ConfigureAwait(false))
            await using (var dst = new FileStream(DownloadFile, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            {
                var buf = new byte[1 << 16];
                int n;
                while ((n = await src.ReadAsync(buf.AsMemory(0, buf.Length), ct).ConfigureAwait(false)) > 0)
                {
                    total += n;
                    if (total > _pin.Bytes) { dst.Close(); TryDeleteFile(DownloadFile); return RainInstallResult.SizeMismatch; }
                    sha.TransformBlock(buf, 0, n, null, 0);
                    await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                    progress?.Report(0.95 * total / _pin.Bytes);
                }
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            if (total != _pin.Bytes) { TryDeleteFile(DownloadFile); return RainInstallResult.SizeMismatch; }
            if (!string.Equals(Convert.ToHexString(sha.Hash!), _pin.Sha256, StringComparison.OrdinalIgnoreCase))
            { TryDeleteFile(DownloadFile); return RainInstallResult.HashMismatch; }
            return RainInstallResult.Installed;
        }

        /// <summary>True when <paramref name="entry"/> resolves inside <paramref name="root"/> (zip-slip guard).</summary>
        public static bool IsInside(string root, string entry)
        {
            if (string.IsNullOrEmpty(entry) || Path.IsPathRooted(entry) || entry.Contains(':')) return false;
            var full = Path.GetFullPath(Path.Combine(root, entry));
            var baseDir = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return full.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Unpacks every entry; false (and the caller drops the folder) on any entry outside it.</summary>
        internal static bool Extract(string zipPath, string target)
        {
            try
            {
                using var zip = ZipFile.OpenRead(zipPath);
                if (zip.Entries.Any(e => !IsInside(target, e.FullName))) return false;
                Directory.CreateDirectory(target);
                foreach (var e in zip.Entries)
                {
                    var path = Path.GetFullPath(Path.Combine(target, e.FullName));
                    if (e.Name.Length == 0) { Directory.CreateDirectory(path); continue; } // a folder entry
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    e.ExtractToFile(path, overwrite: true);
                }
                return true;
            }
            catch (InvalidDataException) { return false; }
        }

        private static HttpClient CreateHttp()
        {
            var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ConditioningControlPanel-RainAssets");
            return http;
        }

        private static async Task<Stream> OpenHttp(string url, CancellationToken ct)
        {
            var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        }

        private static void TryDeleteDir(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch (Exception ex) { Diag.Swallowed(ex); } }
        private static void TryDeleteFile(string path) { try { if (File.Exists(path)) File.Delete(path); } catch (Exception ex) { Diag.Swallowed(ex); } }
    }
}
