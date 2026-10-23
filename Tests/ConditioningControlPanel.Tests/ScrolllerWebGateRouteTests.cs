using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The daily share in WebView2 pages (owner, 2026-10-19): cached Scrolller media is answered from
/// the device (free, Range honoured), uncached media with no share left gets a local 403, pictures
/// and GIFs are fetched whole by the app (teed into the cache, charged as bytes flow), clips and
/// ranged requests go out and are charged by length. API calls are never charged.
/// </summary>
public class ScrolllerWebGateRouteTests
{
    private const string Clip = "https://v.scrolller.com/a/b.mp4";
    private const string Pic = "https://images.scrolller.com/a/b.jpg";
    private const string Gif = "https://images.scrolller.com/a/b.gif";
    private const string Api = "https://api.scrolller.com/api/v2/graphql";

    [Theory]
    // switch off: always the 403, cached or not
    [InlineData(Pic, "GET", false, true, true, false, "Block")]
    [InlineData(Clip, "GET", false, false, false, true, "Block")]
    // cached: answered from the file, even with the share used up, ranged or not
    [InlineData(Pic, "GET", true, true, false, false, "ServeCached")]
    [InlineData(Clip, "GET", true, true, false, true, "ServeCached")]
    [InlineData(Gif, "GET", true, true, true, false, "ServeCached")]
    // spent or away, not cached: local 403
    [InlineData(Pic, "GET", true, false, false, false, "Refuse")]
    [InlineData(Clip, "GET", true, false, false, true, "Refuse")]
    // fresh: pictures and GIFs fetched whole, clips and ranged requests pass through charged
    [InlineData(Pic, "GET", true, false, true, false, "FetchWhole")]
    [InlineData(Gif, "GET", true, false, true, false, "FetchWhole")]
    [InlineData(Clip, "GET", true, false, true, true, "PassCharged")]
    [InlineData(Clip, "GET", true, false, true, false, "PassCharged")]
    [InlineData(Pic, "GET", true, false, true, true, "PassCharged")]
    // the API host keeps the plain rewrite, never charged, never refused
    [InlineData(Api, "POST", true, false, false, false, "Rewrite")]
    [InlineData("https://api.scrolller.com/x.jpg", "GET", true, false, false, false, "Rewrite")]
    // not media, or not a GET: plain rewrite
    [InlineData("https://images.scrolller.com/a/b", "GET", true, false, false, false, "Rewrite")]
    [InlineData(Pic, "HEAD", true, false, false, false, "Rewrite")]
    [InlineData("wss://scrolller.com/socket.mp4", "GET", true, false, false, false, "Rewrite")]
    // not Scrolller: left alone whatever the state
    [InlineData("https://notscrolller.com/a.jpg", "GET", true, false, false, false, "Pass")]
    [InlineData("https://ccp.game/a.mp4", "GET", false, false, false, false, "Pass")]
    public void Routes_each_request(string url, string method, bool enabled, bool cached, bool fresh, bool range, string expected)
        => Assert.Equal(expected, ScrolllerWebGate.Route(url, method, enabled, cached, fresh, range,
            ScrolllerWebGate.KindOf(url)).ToString());

    [Theory]
    [InlineData(Clip, false, false, "Clip")]
    [InlineData("https://v.scrolller.com/a/b.WEBM?x=1", false, false, "Clip")]
    [InlineData(Gif, false, false, "Gif")]
    [InlineData(Pic, false, false, "Picture")]
    [InlineData("https://images.scrolller.com/a/b.webp", false, false, "Picture")]
    [InlineData("https://images.scrolller.com/a/b", true, false, "Clip")]
    [InlineData("https://images.scrolller.com/a/b", false, true, "Picture")]
    [InlineData("https://images.scrolller.com/a/b", false, false, "Other")]
    [InlineData(null, false, false, "Other")]
    public void Kind_comes_from_the_extension_then_the_context(string? url, bool media, bool image, string expected)
        => Assert.Equal(expected, ScrolllerWebGate.KindOf(url, media, image).ToString());

    [Theory]
    [InlineData("bytes=0-99", 1000, 0, 99)]
    [InlineData("bytes=100-", 1000, 100, 999)]
    [InlineData("bytes=0-5000", 1000, 0, 999)]
    [InlineData("bytes=-100", 1000, 900, 999)]
    [InlineData("bytes=-5000", 1000, 0, 999)]
    [InlineData(" BYTES=10-20 ", 1000, 10, 20)]
    public void Parses_a_single_range(string header, long total, long start, long end)
    {
        Assert.True(ScrolllerWebGate.TryParseRange(header, total, out var r, out var bad));
        Assert.False(bad);
        Assert.Equal(new ScrolllerByteRange(start, end), r);
    }

    [Fact]
    public void An_open_range_is_capped_to_a_slice()
    {
        long total = 50L * 1024 * 1024;
        Assert.True(ScrolllerWebGate.TryParseRange("bytes=0-", total, out var r, out _));
        Assert.Equal(ScrolllerWebGate.SliceBytes, r.Length);
        Assert.True(ScrolllerWebGate.TryParseRange("bytes=0-" + (total - 1), total, out var whole, out _));
        Assert.Equal(total, whole.Length);
    }

    [Theory]
    [InlineData("bytes=1000-", 1000)]
    [InlineData("bytes=5000-6000", 1000)]
    [InlineData("bytes=-0", 1000)]
    public void Reports_an_unsatisfiable_range(string header, long total)
    {
        Assert.True(ScrolllerWebGate.TryParseRange(header, total, out _, out var bad));
        Assert.True(bad);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("items=0-10")]
    [InlineData("bytes=0-10,20-30")]
    [InlineData("bytes=abc-")]
    [InlineData("bytes=20-10")]
    [InlineData("bytes=")]
    public void Ignores_ranges_it_does_not_serve(string? header)
        => Assert.False(ScrolllerWebGate.TryParseRange(header, 1000, out _, out _));

    [Fact]
    public void Shapes_a_partial_answer()
    {
        var h = ScrolllerWebGate.ResponseHeaders("video/mp4", 100, new ScrolllerByteRange(0, 99), 1000);
        Assert.Contains("Content-Type: video/mp4\r\n", h);
        Assert.Contains("Content-Length: 100\r\n", h);
        Assert.Contains("Content-Range: bytes 0-99/1000\r\n", h);
        Assert.Contains("Accept-Ranges: bytes", h);
        Assert.Contains("Access-Control-Allow-Origin: *", h);
        Assert.Contains(ScrolllerWebGate.MarkerHeader + ": local", h);
        Assert.False(h.EndsWith("\r\n"));
        Assert.DoesNotContain("Content-Range", ScrolllerWebGate.ResponseHeaders("image/gif", 5));
    }

    [Theory]
    [InlineData("1234", null, 1234)]
    [InlineData(null, "bytes 100-199/5000", 100)]
    [InlineData("", "bytes 0-0/10", 1)]
    [InlineData(null, "bytes */5000", 0)]
    [InlineData(null, null, 0)]
    [InlineData("junk", "junk", 0)]
    public void Charges_a_passed_response_by_its_length(string? length, string? range, long expected)
        => Assert.Equal(expected, ScrolllerWebGate.ChargeableLength(length, range));

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("https://ccp.game/", "https://ccp.game/")]
    [InlineData("https://www.scrolller.com/r/x", ScrolllerWebGate.OurReferer)]
    public void Own_fetch_sends_our_referer_rule(string? page, string? expected)
        => Assert.Equal(expected, ScrolllerWebGate.OutgoingReferer(page));

    private sealed class FakeWriter : IScrolllerCacheWriter
    {
        public MemoryStream Buffer { get; } = new();
        public bool Committed, Disposed, FailWrites;
        public Stream Stream => FailWrites ? throw new IOException("disk full") : Buffer;
        public void Commit() => Committed = true;
        public void Dispose() => Disposed = true;
    }

    private static byte[] Bytes(int n) { var b = new byte[n]; new Random(n).NextBytes(b); return b; }

    [Fact]
    public async Task A_whole_body_is_teed_charged_and_committed()
    {
        var data = Bytes(700_000);
        var sink = new MemoryStream();
        var w = new FakeWriter();
        var charges = new List<long>();
        var whole = await ScrolllerWebGate.TeeCopyAsync(new MemoryStream(data), sink, w, charges.Add, data.Length,
            ScrolllerWebGate.MaxBodyBytes, CancellationToken.None);
        Assert.True(whole);
        Assert.Equal(data, sink.ToArray());
        Assert.Equal(data, w.Buffer.ToArray());
        Assert.True(w.Committed);
        Assert.True(w.Disposed);
        Assert.Equal(data.Length, Sum(charges));
        Assert.True(charges.Count > 1, "charged as bytes flow, not once at the end");
    }

    [Fact]
    public async Task A_short_body_pays_for_what_came_and_never_commits()
    {
        var data = Bytes(300_000);
        var w = new FakeWriter();
        long charged = 0;
        var whole = await ScrolllerWebGate.TeeCopyAsync(new MemoryStream(data), new MemoryStream(), w,
            b => charged += b, data.Length + 10, ScrolllerWebGate.MaxBodyBytes, CancellationToken.None);
        Assert.False(whole);
        Assert.False(w.Committed);
        Assert.True(w.Disposed);
        Assert.Equal(data.Length, charged);
    }

    [Fact]
    public async Task A_cut_download_still_pays()
    {
        var w = new FakeWriter();
        long charged = 0;
        await Assert.ThrowsAsync<IOException>(() => ScrolllerWebGate.TeeCopyAsync(new BreaksAfter(100_000),
            new MemoryStream(), w, b => charged += b, null, ScrolllerWebGate.MaxBodyBytes, CancellationToken.None));
        Assert.Equal(100_000, charged);
        Assert.False(w.Committed);
        Assert.True(w.Disposed);
    }

    [Fact]
    public async Task A_broken_cache_never_breaks_the_page()
    {
        var data = Bytes(200_000);
        var sink = new MemoryStream();
        var w = new FakeWriter { FailWrites = true };
        var whole = await ScrolllerWebGate.TeeCopyAsync(new MemoryStream(data), sink, w, _ => { }, null,
            ScrolllerWebGate.MaxBodyBytes, CancellationToken.None);
        Assert.True(whole);
        Assert.Equal(data, sink.ToArray());
        Assert.False(w.Committed);
        Assert.True(w.Disposed);
    }

    [Fact]
    public async Task No_cache_writer_still_serves_and_charges()
    {
        var data = Bytes(50_000);
        var sink = new MemoryStream();
        long charged = 0;
        Assert.True(await ScrolllerWebGate.TeeCopyAsync(new MemoryStream(data), sink, null, b => charged += b,
            null, ScrolllerWebGate.MaxBodyBytes, CancellationToken.None));
        Assert.Equal(data.Length, sink.Length);
        Assert.Equal(data.Length, charged);
    }

    private static long Sum(List<long> xs) { long s = 0; foreach (var x in xs) s += x; return s; }

    private sealed class BreaksAfter : Stream
    {
        private readonly long _limit; private long _done;
        public BreaksAfter(long limit) => _limit = limit;
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_done >= _limit) throw new IOException("connection reset");
            var n = (int)Math.Min(count, _limit - _done);
            _done += n;
            return n;
        }
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _done; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }
}
