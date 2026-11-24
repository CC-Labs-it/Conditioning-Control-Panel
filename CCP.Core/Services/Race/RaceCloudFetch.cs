using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.Race;

public static class RaceCloudSite
{
    public static bool IsSiteUri(string? uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var u) &&
        u.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) &&
        (u.Host.Equals("bambicloud.com", StringComparison.OrdinalIgnoreCase) ||
         u.Host.EndsWith(".bambicloud.com", StringComparison.OrdinalIgnoreCase));
}

/// <summary>Host-independent cloud track probe and temporary download.</summary>
public sealed class RaceCloudFetch : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _root;

    public RaceCloudFetch(string userDataPath, HttpMessageHandler? handler = null)
    {
        if (handler is HttpClientHandler supplied) supplied.AllowAutoRedirect = false;
        _http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(15) };
        _root = Path.Combine(userDataPath, "race", "cloud");
    }

    public void Dispose() => _http.Dispose();

    public async Task<string?> ProbeHashAsync(string src, CancellationToken ct = default)
    {
        if (!RaceCloudSite.IsSiteUri(src)) return null;
        try
        {
            long? length = await LengthAsync(src, ct).ConfigureAwait(false);
            if (length is not > 0) return null;
            int want = (int)Math.Min(TrackDecoder.HashHead, length.Value);
            using var req = new HttpRequestMessage(HttpMethod.Get, src);
            req.Headers.Range = new RangeHeaderValue(0, want - 1);
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (resp.StatusCode != HttpStatusCode.PartialContent) return null;
            var head = new byte[want];
            using var body = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            int filled = 0;
            while (filled < want)
            {
                int got = await body.ReadAsync(head.AsMemory(filled, want - filled), ct).ConfigureAwait(false);
                if (got == 0) return null;
                filled += got;
            }
            return TrackDecoder.HashBytes(length.Value, head);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { return null; }
    }

    private async Task<long?> LengthAsync(string src, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Head, src);
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode && resp.Content.Headers.ContentLength is > 0)
                return resp.Content.Headers.ContentLength;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { }
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, src);
            req.Headers.Range = new RangeHeaderValue(0, 0);
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            return resp.Content.Headers.ContentRange?.Length;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { return null; }
    }

    public async Task<string> DownloadAsync(string src, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (!RaceCloudSite.IsSiteUri(src)) throw new InvalidOperationException("the audio is not an address we may fetch");
        Directory.CreateDirectory(_root);
        string path = Path.Combine(_root, "cloud-" + Guid.NewGuid().ToString("N") + CloudExtension(src));
        Exception? last = null;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                using var resp = await _http.GetAsync(src, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                long? total = resp.Content.Headers.ContentLength;
                using var body = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
                var buffer = new byte[1 << 16];
                long got = 0;
                int read;
                while ((read = await body.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    got += read;
                    if (total is > 0) progress?.Report((double)got / total.Value);
                }
                await file.FlushAsync(ct).ConfigureAwait(false);
                return path;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { DeleteCloudTemp(path); throw; }
            catch (Exception ex) { last = ex; DeleteCloudTemp(path); }
        }
        throw last ?? new IOException("the audio would not come down");
    }

    public static string CloudExtension(string src)
    {
        try
        {
            string ext = Path.GetExtension(new Uri(src).AbsolutePath).ToLowerInvariant();
            return ext is ".mp3" or ".m4a" or ".wav" or ".ogg" or ".flac" or ".wma" ? ext : ".mp3";
        }
        catch { return ".mp3"; }
    }

    public static void DeleteCloudTemp(string? path)
    {
        try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); }
        catch (Exception) { }
    }

    public void SweepCloudTemp()
    {
        try
        {
            if (!Directory.Exists(_root)) return;
            var cutoff = DateTime.UtcNow.AddHours(-6);
            foreach (string file in Directory.GetFiles(_root, "cloud-*"))
            {
                try { if (File.GetLastWriteTimeUtc(file) < cutoff) File.Delete(file); }
                catch (Exception) { }
            }
        }
        catch (Exception) { }
    }
}
