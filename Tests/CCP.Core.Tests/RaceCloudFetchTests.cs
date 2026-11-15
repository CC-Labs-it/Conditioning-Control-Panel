using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Race;
using Xunit;

namespace CCP.Core.Tests;

public sealed class RaceCloudFetchTests : IDisposable
{
    private readonly string data = Path.Combine(Path.GetTempPath(), "cloud-test-" + Guid.NewGuid().ToString("N"));
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(respond(request));
        }
    }
    [Theory]
    [InlineData("http://bambicloud.com/a")]
    [InlineData("https://example.com/a")]
    [InlineData("https://evilbambicloud.com/a")]
    [InlineData("https://bambicloud.com.example.com/a")]
    public async Task RejectsOffSiteBeforeRequest(string url)
    {
        using var handler = new Handler(_ => throw new Exception());
        using var fetch = new RaceCloudFetch(data, handler);
        Assert.False(RaceCloudSite.IsSiteUri(url));
        Assert.Null(await fetch.ProbeHashAsync(url));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fetch.DownloadAsync(url));
        Assert.Equal(0, handler.Calls);
        Assert.True(RaceCloudSite.IsSiteUri("https://audio.bambicloud.com/a"));
    }
    [Fact]
    public async Task ProbeMatchesFileAndRejectsIgnoredRange()
    {
        byte[] bytes = new byte[1024 * 1024 + 32];
        new Random(42).NextBytes(bytes);
        Directory.CreateDirectory(data);
        string path = Path.Combine(data, "track");
        await File.WriteAllBytesAsync(path, bytes);
        bool ignore = false;
        using var handler = new Handler(req => {
            var content = new ByteArrayContent(req.Method == HttpMethod.Head ? Array.Empty<byte>() : bytes[..(1024 * 1024)]);
            if (req.Method == HttpMethod.Head) content.Headers.ContentLength = bytes.Length;
            return new HttpResponseMessage(req.Method == HttpMethod.Head || ignore ? HttpStatusCode.OK : HttpStatusCode.PartialContent) { Content = content };
        });
        using var fetch = new RaceCloudFetch(data, handler);
        Assert.Equal(TrackDecoder.HashFile(path), await fetch.ProbeHashAsync("https://bambicloud.com/a"));
        ignore = true;
        Assert.Null(await fetch.ProbeHashAsync("https://bambicloud.com/a"));
    }
    [Fact]
    public async Task ProbeFallsBackToContentRangeAndRejectsShortRead()
    {
        byte[] bytes = { 1, 2, 3, 4 };
        bool shortRead = false;
        using var handler = new Handler(req => {
            if (req.Method == HttpMethod.Head) return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
            if (req.Headers.Range!.Ranges.First().To == 0)
            {
                var one = new ByteArrayContent(new byte[] { 1 });
                one.Headers.ContentRange = new ContentRangeHeaderValue(0, 0, bytes.Length);
                return new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = one };
            }
            return new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(shortRead ? bytes[..2] : bytes) };
        });
        using var fetch = new RaceCloudFetch(data, handler);
        Assert.Equal(TrackDecoder.HashBytes(bytes.Length, bytes), await fetch.ProbeHashAsync("https://bambicloud.com/a"));
        shortRead = true;
        Assert.Null(await fetch.ProbeHashAsync("https://bambicloud.com/a"));
    }
    [Fact]
    public async Task DownloadReportsProgressAndUsesCloudDirectory()
    {
        using var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1, 2, 3 }) });
        double progress = 0;
        using var fetch = new RaceCloudFetch(data, handler);
        string path = await fetch.DownloadAsync("https://bambicloud.com/a.wav", new InlineProgress(value => progress = value));
        Assert.StartsWith(Path.Combine(data, "race", "cloud", "cloud-"), path);
        Assert.EndsWith(".wav", path);
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(path));
        Assert.Equal(1, progress);
    }
    private sealed class InlineProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
    private sealed class BrokenStream : MemoryStream
    {
        private bool read;
        public BrokenStream() : base(new byte[] { 1, 2, 3 }) { }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (read) throw new IOException("broken stream");
            read = true;
            return base.ReadAsync(buffer, ct);
        }
    }
    [Fact]
    public async Task FailedDownloadRetriesOnceAndCancellationThrows()
    {
        using var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new BrokenStream()) });
        using var fetch = new RaceCloudFetch(data, handler);
        await Assert.ThrowsAsync<IOException>(() => fetch.DownloadAsync("https://bambicloud.com/a"));
        Assert.Equal(2, handler.Calls);
        Assert.Empty(Directory.GetFiles(Path.Combine(data, "race", "cloud")));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetch.DownloadAsync("https://bambicloud.com/a", ct: cts.Token));
    }
    [Fact]
    public void SweepOnlyOldCloudFiles()
    {
        string root = Path.Combine(data, "race", "cloud");
        Directory.CreateDirectory(root);
        string old = Path.Combine(root, "cloud-old"), fresh = Path.Combine(root, "cloud-fresh"), other = Path.Combine(root, "other");
        File.WriteAllText(old, "a"); File.WriteAllText(fresh, "b"); File.WriteAllText(other, "c");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddHours(-7));
        File.SetLastWriteTimeUtc(other, DateTime.UtcNow.AddHours(-7));
        using var fetch = new RaceCloudFetch(data);
        fetch.SweepCloudTemp();
        Assert.False(File.Exists(old)); Assert.True(File.Exists(fresh)); Assert.True(File.Exists(other));
        Assert.Equal(".ogg", RaceCloudFetch.CloudExtension("https://bambicloud.com/a.OGG?q=x"));
        Assert.Equal(".mp3", RaceCloudFetch.CloudExtension("https://bambicloud.com/a.bin"));
        RaceCloudFetch.DeleteCloudTemp(fresh);
        Assert.False(File.Exists(fresh));
    }
    public void Dispose() { if (Directory.Exists(data)) Directory.Delete(data, true); }
}
