using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The resumable download lifted out of ReleaseContentService (content packs, the T&amp;C Expo):
/// ranged resume into the kept <c>.partial</c>, a restart when the server ignores Range, 404, progress, cancel.</summary>
public class ResumableDownloadTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ccp-resumable-" + Guid.NewGuid().ToString("N"));
    private static readonly byte[] Payload = Enumerable.Range(0, 300_000).Select(i => (byte)(i * 7)).ToArray();

    public ResumableDownloadTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    /// <summary>Serves <see cref="Payload"/>, honouring Range unless told not to; records the ranges asked.</summary>
    private sealed class FakeServer : HttpMessageHandler
    {
        public bool HonourRange = true;
        public HttpStatusCode? Status;
        public readonly List<long?> RangesAsked = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var from = request.Headers.Range?.Ranges.First().From;
            RangesAsked.Add(from);
            if (Status is { } s) return Task.FromResult(new HttpResponseMessage(s));
            if (from is long f && HonourRange)
            {
                var r = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(Payload[(int)f..]) };
                r.Content.Headers.ContentRange = new ContentRangeHeaderValue(f, Payload.Length - 1, Payload.Length);
                return Task.FromResult(r);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Payload) });
        }
    }

    private static ResumableDownload With(FakeServer s) => new(new HttpClient(s), "Test", maxAttempts: 2);

    private string Partial => Path.Combine(_dir, "pack.zip.partial");

    [Fact]
    public async Task A_fresh_download_writes_every_byte_and_ends_at_100()
    {
        var server = new FakeServer();
        var seen = new List<double>();
        Assert.True(await With(server).ToPartialAsync("p", "https://example.test/p.zip", Payload.Length, Partial,
            (pct, _, _) => seen.Add(pct), CancellationToken.None));
        Assert.Equal(Payload, File.ReadAllBytes(Partial));
        Assert.Equal(new long?[] { null }, server.RangesAsked);
        Assert.Equal(100, seen.Last());
    }

    [Fact]
    public async Task A_kept_partial_resumes_with_a_range()
    {
        File.WriteAllBytes(Partial, Payload[..100_000]);
        var server = new FakeServer();
        Assert.True(await With(server).ToPartialAsync("p", "https://example.test/p.zip", Payload.Length, Partial, null, CancellationToken.None));
        Assert.Equal(new long?[] { 100_000 }, server.RangesAsked);
        Assert.Equal(Payload, File.ReadAllBytes(Partial));
    }

    [Fact]
    public async Task A_server_that_ignores_range_restarts_from_zero()
    {
        File.WriteAllBytes(Partial, Payload[..100_000]);
        var server = new FakeServer { HonourRange = false };
        Assert.True(await With(server).ToPartialAsync("p", "https://example.test/p.zip", Payload.Length, Partial, null, CancellationToken.None));
        Assert.Equal(Payload, File.ReadAllBytes(Partial));
    }

    [Fact]
    public async Task A_complete_partial_needs_no_request()
    {
        File.WriteAllBytes(Partial, Payload);
        var server = new FakeServer();
        Assert.True(await With(server).ToPartialAsync("p", "https://example.test/p.zip", Payload.Length, Partial, null, CancellationToken.None));
        Assert.Empty(server.RangesAsked);
    }

    [Fact]
    public async Task A_partial_longer_than_advertised_starts_clean()
    {
        File.WriteAllBytes(Partial, new byte[Payload.Length + 10]);
        var server = new FakeServer();
        Assert.True(await With(server).ToPartialAsync("p", "https://example.test/p.zip", Payload.Length, Partial, null, CancellationToken.None));
        Assert.Equal(new long?[] { null }, server.RangesAsked);
        Assert.Equal(Payload, File.ReadAllBytes(Partial));
    }

    [Fact]
    public async Task A_404_is_false_and_keeps_the_partial()
    {
        File.WriteAllBytes(Partial, Payload[..10]);
        var server = new FakeServer { Status = HttpStatusCode.NotFound };
        Assert.False(await With(server).ToPartialAsync("p", "https://example.test/p.zip", Payload.Length, Partial, null, CancellationToken.None));
        Assert.Equal(10, new FileInfo(Partial).Length);
    }

    [Fact]
    public async Task Cancel_throws_and_keeps_the_partial()
    {
        File.WriteAllBytes(Partial, Payload[..10]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            With(new FakeServer()).ToPartialAsync("p", "https://example.test/p.zip", Payload.Length, Partial, null, cts.Token));
        Assert.Equal(10, new FileInfo(Partial).Length);
    }

    [Fact]
    public async Task Sha256_is_lower_case_hex_of_the_file()
    {
        File.WriteAllBytes(Partial, Payload);
        var expected = Convert.ToHexString(SHA256.HashData(Payload)).ToLowerInvariant();
        Assert.Equal(expected, await ResumableDownload.Sha256Async(Partial, CancellationToken.None));
    }

    [Fact]
    public void The_content_packs_call_the_lifted_class()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Services", "Content")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var src = SourceRoots.ReadProductFile("Services", "Content", "ReleaseContentService.cs");
        Assert.Contains("new ResumableDownload(_httpClient, \"ReleaseContentService\", MaxDownloadAttempts)", src);
        Assert.Contains("ResumableDownload.Sha256Async(path, ct)", src);
    }
}
