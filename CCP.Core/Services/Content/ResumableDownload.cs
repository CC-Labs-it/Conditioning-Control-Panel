using System;
using Serilog;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The resumable download the content packs use, lifted out of <see cref="ReleaseContentService"/> as a
    /// pure move so the T&amp;C Llama Expo can share it: a ranged download into a <c>.partial</c> file that
    /// survives a transient failure or a killed app, with backoff, a progress callback, a cancel token,
    /// and a sha256 of the finished file. The caller owns the manifest, the room check, the verify and the
    /// install; this class only moves bytes.
    /// </summary>
    internal sealed class ResumableDownload
    {
        private readonly HttpClient _httpClient;
        private readonly string _tag;
        private readonly int _maxAttempts;

        /// <param name="httpClient">The caller's client (its timeout and user agent apply).</param>
        /// <param name="logTag">Log prefix, so a caller's lines read as before (e.g. "ReleaseContentService").</param>
        /// <param name="maxAttempts">Download attempts before giving up; the partial is kept between them.</param>
        public ResumableDownload(HttpClient httpClient, string logTag, int maxAttempts)
        {
            _httpClient = httpClient;
            _tag = logTag;
            _maxAttempts = maxAttempts;
        }

        /// <summary>
        /// Ranged/resumable download loop with backoff, modelled on ContentPackService's transport:
        /// the <c>.partial</c> file is never deleted on a transient failure, so a killed app (or a
        /// dropped connection) resumes where it left off. GitHub redirects release-asset URLs to
        /// objects.githubusercontent.com, which honours Range on the redirected request.
        /// <paramref name="report"/> gets (percent, received, total) in whole-percent steps.
        /// </summary>
        public async Task<bool> ToPartialAsync(
            string id, string url, long expectedBytes, string partialPath, Action<double, long, long>? report, CancellationToken ct)
        {
            var totalBytes = expectedBytes;
            var retryDelay = TimeSpan.FromSeconds(3);

            for (var attempt = 1; attempt <= _maxAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    long resumeFrom = 0;
                    if (File.Exists(partialPath))
                    {
                        resumeFrom = new FileInfo(partialPath).Length;
                        if (totalBytes > 0 && resumeFrom > totalBytes)
                        {
                            // Longer than advertised — the asset changed under us. Start clean.
                            Log.Warning("{Tag}: partial for {Pack} is larger than the manifest size — restarting",
                                _tag, id);
                            TryDeleteFile(partialPath);
                            resumeFrom = 0;
                        }
                        else if (totalBytes > 0 && resumeFrom == totalBytes)
                        {
                            // Already complete from a previous session; let the caller verify it.
                            report?.Invoke(100, resumeFrom, totalBytes);
                            return true;
                        }
                        else if (resumeFrom > 0)
                        {
                            Log.Information("{Tag}: resuming {Pack} from byte {Byte} (attempt {Attempt})",
                                _tag, id, resumeFrom, attempt);
                        }
                    }

                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    if (resumeFrom > 0)
                    {
                        request.Headers.Range = new RangeHeaderValue(resumeFrom, null);
                    }

                    using var response = await _httpClient
                        .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                        .ConfigureAwait(false);

                    if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                    {
                        // The server thinks we already hold the whole file. Verify upstream.
                        Log.Information("{Tag}: server reports {Pack} range complete", _tag, id);
                        return true;
                    }
                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        Log.Warning("{Tag}: pack asset 404 for {Pack} at {Host}", _tag, id, Logging.UrlLog.Host(url));
                        return false;
                    }
                    if (response.StatusCode != HttpStatusCode.OK && response.StatusCode != HttpStatusCode.PartialContent)
                    {
                        response.EnsureSuccessStatusCode();
                    }

                    // A 200 to a ranged request means the server ignored Range — rewrite from zero.
                    var serverHonouredRange = response.StatusCode == HttpStatusCode.PartialContent;
                    if (resumeFrom > 0 && !serverHonouredRange)
                    {
                        Log.Information("{Tag}: server ignored Range for {Pack} — restarting download", _tag, id);
                        resumeFrom = 0;
                    }

                    var contentRangeLength = response.Content.Headers.ContentRange?.Length;
                    var contentLength = response.Content.Headers.ContentLength;
                    if (contentRangeLength.HasValue)
                        totalBytes = contentRangeLength.Value;
                    else if (contentLength.HasValue)
                        totalBytes = resumeFrom + contentLength.Value;

                    var fileMode = resumeFrom > 0 ? FileMode.Append : FileMode.Create;
                    var received = resumeFrom;

                    using (var contentStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
                    using (var fileStream = new FileStream(partialPath, fileMode, FileAccess.Write, FileShare.None, 65536, useAsync: true))
                    {
                        var buffer = new byte[65536];
                        int read;
                        var lastReported = -1.0;
                        while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
                            received += read;

                            if (totalBytes > 0)
                            {
                                var pct = Math.Min(100.0, received * 100.0 / totalBytes);
                                // Throttle to whole-percent steps — these events can reach the UI thread.
                                if (pct - lastReported >= 1.0 || pct >= 100.0)
                                {
                                    lastReported = pct;
                                    report?.Invoke(pct, received, totalBytes);
                                }
                            }
                        }
                    }

                    var finalSize = new FileInfo(partialPath).Length;
                    if (totalBytes > 0 && finalSize < totalBytes)
                    {
                        throw new IOException($"Download incomplete: received {finalSize} of {totalBytes} bytes");
                    }

                    report?.Invoke(100, finalSize, totalBytes > 0 ? totalBytes : finalSize);
                    Log.Information("{Tag}: downloaded {Pack} ({Bytes} bytes)", _tag, id, finalSize);
                    return true;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (attempt < _maxAttempts
                                           && (ex is HttpRequestException || ex is TaskCanceledException || ex is IOException))
                {
                    var currentSize = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
                    Log.Warning(
                        "{Tag}: {Pack} attempt {Attempt}/{Max} failed at {Bytes} bytes: {Error}",
                        _tag, id, attempt, _maxAttempts, currentSize, ex.Message);

                    // Keep the partial — the next attempt resumes from it.
                    await Task.Delay(retryDelay, ct).ConfigureAwait(false);
                    retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 1.5, 30));
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "{Tag}: {Pack} download failed permanently", _tag, id);
                    return false;
                }
            }

            Log.Warning("{Tag}: {Pack} exhausted {Max} download attempts", _tag, id, _maxAttempts);
            return false;
        }

        /// <summary>Lower-case hex sha256 of a file, off the calling thread.</summary>
        public static async Task<string> Sha256Async(string path, CancellationToken ct)
        {
            return await Task.Run(() =>
            {
                using var sha = SHA256.Create();
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
                var hash = sha.ComputeHash(stream);
                return Convert.ToHexString(hash).ToLowerInvariant();
            }, ct).ConfigureAwait(false);
        }

        private static void TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
