using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Audio;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The audio-sync download + chunk pipeline moved from the WPF head (VideoDownloader, ChunkManager,
/// App.GetMediaTempPath -> CorePaths.MediaTemp), driven against a loopback video and a fake decoder.</summary>
[Collection(SessionStatics.Name)]
public sealed class AudioChunkPipelineTests : IDisposable
{
    private HttpListener _listener = new();
    private readonly string _baseUrl = "";
    private readonly byte[] _video = new byte[5000];
    private readonly string _assets = Path.Combine(Path.GetTempPath(), "ccp-audio-chunk-" + Guid.NewGuid().ToString("N"));
    private readonly Func<string>? _oldProvider = CorePaths.EffectiveAssetsProvider;

    public AudioChunkPipelineTests()
    {
        new Random(7).NextBytes(_video);
        // HttpListener cannot bind port 0, so it claims a random port itself and retries on a clash
        // (no reserve-then-close window another process could steal).
        var rng = new Random();
        for (var attempt = 0; ; attempt++)
        {
            _baseUrl = $"http://127.0.0.1:{rng.Next(20000, 60000)}";
            _listener.Prefixes.Add(_baseUrl + "/");
            try { _listener.Start(); break; }
            catch (HttpListenerException) when (attempt < 50)
            {
                _listener = new HttpListener();
            }
        }
        _ = ServeAsync();
        Directory.CreateDirectory(_assets);
        CorePaths.EffectiveAssetsProvider = () => _assets;
    }

    private async Task ServeAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); } catch { return; }
            ctx.Response.ContentLength64 = _video.Length;
            await ctx.Response.OutputStream.WriteAsync(_video);
            ctx.Response.Close();
        }
    }

    public void Dispose()
    {
        _listener.Close();
        CorePaths.EffectiveAssetsProvider = _oldProvider;
        try { Directory.Delete(_assets, true); } catch { }
    }

    private static AudioSyncSettings Settings() => new() { ChunkDurationSeconds = 60 };

    [Fact]
    public void MediaTemp_IsAssetsDotTemp_AndFallsBackToSystemTempWhenAssetsFail()
    {
        var expected = Path.Combine(_assets, ".temp");
        Assert.Equal(expected, CorePaths.MediaTemp());
        Assert.True(Directory.Exists(expected));

        CorePaths.EffectiveAssetsProvider = () => throw new IOException("no assets");
        Assert.Equal(Path.GetTempPath(), CorePaths.MediaTemp());
    }

    [Theory]
    [InlineData("https://x.test/clip.MP4?sig=1", true)]
    [InlineData("https://x.test/a.webm", true)]
    [InlineData("https://cdn.x.test/abc", true)]
    [InlineData("https://x.test/video/123", true)]
    [InlineData("https://x.test/page.html", false)]
    [InlineData("", false)]
    public void IsLikelyVideoUrl_MatchesExtensionsAndCdnPatterns(string url, bool expected)
        => Assert.Equal(expected, VideoDownloader.IsLikelyVideoUrl(url));

    [Fact]
    public void DownloadProgress_PercentIsNullWithoutATotal()
    {
        Assert.Equal(25, new DownloadProgressEventArgs(250, 1000).PercentComplete);
        Assert.Null(new DownloadProgressEventArgs(250, null).PercentComplete);
    }

    [Fact]
    public async Task DownloadAsync_WritesTheVideoIntoMediaTemp_AndReportsTheFinalTotal()
    {
        using var downloader = new VideoDownloader();
        DownloadProgressEventArgs? last = null;
        downloader.ProgressChanged += (_, e) => last = e;

        var path = await downloader.DownloadAsync(_baseUrl + "/v.mp4", TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(_assets, ".temp"), Path.GetDirectoryName(path));
        Assert.StartsWith("haptic_video_", Path.GetFileName(path));
        Assert.Equal(_video, File.ReadAllBytes(path));
        Assert.Equal(100, last!.PercentComplete);
        File.Delete(path);
    }

    [Fact]
    public void Initialize_SplitsTheDurationIntoChunks_LastOneClamped()
    {
        using var chunks = new ChunkManager(Settings(), (_, _, _, _) => Array.Empty<float>());
        chunks.Initialize(_baseUrl + "/v.mp4", TimeSpan.FromSeconds(150));

        Assert.Equal(0, chunks.GetChunkIndexForTime(TimeSpan.FromSeconds(59)));
        Assert.Equal(2, chunks.GetChunkIndexForTime(TimeSpan.FromSeconds(130)));
        Assert.False(chunks.IsFirstChunkReady);
        Assert.False(chunks.IsChunkReady(3));
    }

    [Fact]
    public async Task FirstChunk_DownloadsOnce_DecodesItsRangeOnly_AndFillsTheTrack()
    {
        var calls = new List<(string Path, TimeSpan Start, TimeSpan End)>();
        float[] Decode(string path, TimeSpan start, TimeSpan end, System.Threading.CancellationToken _)
        {
            lock (calls) calls.Add((path, start, end));
            var samples = new float[44100 * 2];
            for (int i = 0; i < samples.Length; i++) samples[i] = (float)Math.Sin(i * 0.05) * 0.8f;
            return samples;
        }

        var chunks = new ChunkManager(Settings(), Decode);
        AudioChunk? ready = null;
        chunks.ChunkReady += (_, c) => ready = c;
        chunks.Initialize(_baseUrl + "/v.mp4", TimeSpan.FromSeconds(150));

        await chunks.StartFirstChunkAsync(TestContext.Current.CancellationToken);
        await chunks.EnsureChunkReadyAsync(2, TestContext.Current.CancellationToken);

        Assert.True(chunks.IsFirstChunkReady);
        Assert.True(chunks.IsChunkReady(2));
        Assert.False(chunks.IsChunkReady(1));
        Assert.Equal(2, ready!.Index);
        Assert.True(chunks.Track.IsChunkLoaded(0));
        Assert.Equal(2, calls.Count);
        Assert.Equal(calls[0].Path, calls[1].Path); // downloaded once, reused for every chunk
        Assert.Equal((TimeSpan.Zero, TimeSpan.FromSeconds(60)), (calls[0].Start, calls[0].End));
        Assert.Equal((TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(150)), (calls[1].Start, calls[1].End));
        Assert.Equal(_video, File.ReadAllBytes(calls[0].Path));

        chunks.Dispose();
        Assert.False(File.Exists(calls[0].Path)); // the cached video goes with the manager
    }

    [Fact]
    public async Task EmptyDecode_LeavesTheChunkNotReady_AndRaisesNoChunkReady()
    {
        using var chunks = new ChunkManager(Settings(), (_, _, _, _) => Array.Empty<float>());
        var raised = false;
        chunks.ChunkReady += (_, _) => raised = true;
        chunks.Initialize(_baseUrl + "/v.mp4", TimeSpan.FromSeconds(90));

        await chunks.StartFirstChunkAsync(TestContext.Current.CancellationToken);

        Assert.False(chunks.IsFirstChunkReady);
        Assert.False(raised);
    }
}
