using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Review fixes on the Scrolller web gate (2026-10-19): a passed-through clip pays for the bytes
/// that really arrived (DevTools Network domain), a stalled whole fetch gives up, a request with no
/// Range on a cached file gets the whole file (never a cut 206), the gate's own refusals carry
/// CORS, only image bodies go into the cache, and a full book of passed-on requests never drops
/// one still in flight.
/// </summary>
public class ScrolllerGateRealBytesTests
{
    private const long Batch = ScrolllerWebGate.ChargeBatchBytes;

    private static (ScrolllerRealBytesMeter Meter, List<(string Url, long Bytes, long? Total)> Charges) NewMeter()
    {
        var charges = new List<(string, long, long?)>();
        return (new ScrolllerRealBytesMeter((u, b, t) => charges.Add((u, b, t))), charges);
    }

    // ---- the meter ------------------------------------------------------------------------

    [Fact]
    public void A_cut_clip_pays_for_what_arrived_not_its_declared_length()
    {
        var (m, charges) = NewMeter();
        m.Start("1.1", "https://v.scrolller.com/a.mp4", 50_000_000);
        m.Data("1.1", 100_000);
        m.Data("1.1", 100_000);
        Assert.Empty(charges);                       // under one batch: nothing yet
        m.Data("1.1", 100_000);                      // 300 KB seen: one batch charged
        Assert.Single(charges);
        Assert.Equal(300_000, charges[0].Bytes);
        Assert.Equal(50_000_000L, charges[0].Total);
        m.Data("1.1", 40_000);
        m.End("1.1", null);                          // loadingFailed (the player cut it)
        Assert.Equal(340_000, charges[0].Bytes + charges[1].Bytes);
        Assert.Equal(0, m.LiveCount);
    }

    [Fact]
    public void Finished_pays_the_larger_of_the_running_sum_and_its_own_total()
    {
        var (m, charges) = NewMeter();
        m.Start("2.1", "https://v.scrolller.com/b.mp4", null);
        m.Data("2.1", 1000);
        m.End("2.1", 1500);                          // headers counted in the finished total
        Assert.Equal(1500, Assert.Single(charges).Bytes);

        charges.Clear();
        m.Start("2.2", "https://v.scrolller.com/b.mp4", null);
        m.End("2.2", 0);                             // a replay from Chromium's cache: free
        Assert.Empty(charges);
    }

    [Fact]
    public void Events_for_requests_it_never_started_are_ignored()
    {
        var (m, charges) = NewMeter();
        m.Data("x", Batch * 4);
        m.End("x", Batch * 4);
        Assert.Empty(charges);
    }

    [Fact]
    public void Open_requests_past_the_cap_are_settled_then_dropped()
    {
        var (m, charges) = NewMeter();
        for (int i = 0; i < ScrolllerRealBytesMeter.MaxLive; i++)
        {
            m.Start("r" + i, "https://v.scrolller.com/c.mp4", null);
            m.Data("r" + i, 10);
        }
        m.Start("next", "https://v.scrolller.com/d.mp4", null);
        Assert.Equal(ScrolllerRealBytesMeter.MaxLive, charges.Count);
        Assert.Equal(1, m.LiveCount);
    }

    // ---- DevTools payloads ----------------------------------------------------------------

    private const string Response206 =
        "{\"requestId\":\"77.12\",\"type\":\"Media\",\"response\":{\"url\":\"https://v.scrolller.com/a/b.mp4\"," +
        "\"status\":206,\"headers\":{\"content-range\":\"bytes 0-1048575/9000000\",\"content-length\":\"1048576\"}," +
        "\"fromDiskCache\":false}}";

    [Fact]
    public void Reads_a_responseReceived_payload()
    {
        var r = ScrolllerWebGate.ParseCdpResponse(Response206);
        Assert.NotNull(r);
        Assert.Equal("77.12", r!.RequestId);
        Assert.Equal("https://v.scrolller.com/a/b.mp4", r.Url);
        Assert.Equal(206, r.Status);
        Assert.Equal(9_000_000L, r.Total);
        Assert.False(r.OurAnswer);
        Assert.True(ScrolllerWebGate.ShouldMeter(r));
    }

    [Fact]
    public void Never_meters_its_own_answers_errors_or_the_api()
    {
        var ours = ScrolllerWebGate.ParseCdpResponse(Response206.Replace("\"content-length\"", "\"X-CCP-Gate\":\"local\",\"content-length\""));
        Assert.True(ours!.OurAnswer);
        Assert.False(ScrolllerWebGate.ShouldMeter(ours));

        // A range served partly from Chromium's cache still meters: its encoded bytes are only
        // what came over the network (probe: 12.9 MB arrived on a fromDiskCache:true 206).
        var partlyCached = ScrolllerWebGate.ParseCdpResponse(Response206.Replace("\"fromDiskCache\":false", "\"fromDiskCache\":true"));
        Assert.True(ScrolllerWebGate.ShouldMeter(partlyCached!));

        var refused = ScrolllerWebGate.ParseCdpResponse(Response206.Replace("\"status\":206", "\"status\":403"));
        Assert.False(ScrolllerWebGate.ShouldMeter(refused!));

        var api = ScrolllerWebGate.ParseCdpResponse(Response206.Replace("https://v.scrolller.com/a/b.mp4", "https://api.scrolller.com/api/v2/graphql"));
        Assert.False(ScrolllerWebGate.ShouldMeter(api!));

        Assert.Null(ScrolllerWebGate.ParseCdpResponse(Response206.Replace("v.scrolller.com", "ccp.game")));
        Assert.Null(ScrolllerWebGate.ParseCdpResponse(""));
    }

    [Fact]
    public void Reads_ids_and_lengths_from_data_and_finish_events()
    {
        Assert.True(ScrolllerWebGate.TryReadIdAndLength(
            "{\"requestId\":\"9.1\",\"timestamp\":1.5,\"dataLength\":0,\"encodedDataLength\":65536}", "encodedDataLength", out var id, out var n));
        Assert.Equal("9.1", id);
        Assert.Equal(65536, n);
        Assert.True(ScrolllerWebGate.TryReadIdAndLength(
            "{\"requestId\":\"9.2\",\"timestamp\":1.5,\"type\":\"Media\",\"errorText\":\"net::ERR_ABORTED\",\"canceled\":true}", null, out id, out n));
        Assert.Equal("9.2", id);
        Assert.Equal(0, n);
        Assert.False(ScrolllerWebGate.TryReadIdAndLength("{\"timestamp\":1}", null, out _, out _));
    }

    [Fact]
    public void Network_enable_keeps_no_response_bodies()
        => Assert.Contains("\"maxResourceBufferSize\":0", ScrolllerWebGate.NetworkEnableParams);

    // ---- passed-on marks ------------------------------------------------------------------

    [Fact]
    public void A_passed_url_is_charged_once_per_request()
    {
        var url = "https://v.scrolller.com/pass/" + Guid.NewGuid().ToString("N") + ".mp4";
        Assert.False(ScrolllerWebGate.TakePassed(url));
        ScrolllerWebGate.MarkPassed(url);
        ScrolllerWebGate.MarkPassed(url);
        Assert.True(ScrolllerWebGate.TakePassed(url));
        Assert.True(ScrolllerWebGate.TakePassed(url));
        Assert.False(ScrolllerWebGate.TakePassed(url));
    }

    [Fact]
    public void An_overfull_book_never_drops_a_request_still_in_flight()
    {
        var first = "https://v.scrolller.com/flood/" + Guid.NewGuid().ToString("N") + ".mp4";
        ScrolllerWebGate.MarkPassed(first);
        for (int i = 0; i < 2500; i++)
            ScrolllerWebGate.MarkPassed("https://v.scrolller.com/flood/" + Guid.NewGuid().ToString("N") + ".mp4");
        Assert.True(ScrolllerWebGate.TakePassed(first));
    }

    // ---- whole fetch: stalls, cache types --------------------------------------------------

    private sealed class Stalls : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set { } }
        public override void Flush() { }
        public override int Read(byte[] b, int o, int c) => throw new NotSupportedException();
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) { }
        public override void Write(byte[] b, int o, int c) { }
        private int _reads;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_reads++ == 0) { buffer.Span[0] = 1; return 1; }
            await Task.Delay(Timeout.Infinite, ct);
            return 0;
        }
    }

    [Fact]
    public async Task A_stalled_body_times_out_and_still_pays_for_what_came()
    {
        long charged = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ScrolllerWebGate.TeeCopyAsync(new Stalls(),
            new MemoryStream(), null, b => charged += b, null, ScrolllerWebGate.MaxBodyBytes, CancellationToken.None,
            TimeSpan.FromMilliseconds(150)));
        Assert.True(sw.ElapsedMilliseconds < 5000, $"took {sw.ElapsedMilliseconds} ms");
        Assert.Equal(1, charged);
    }

    [Fact]
    public void Whole_fetch_bounds_are_set()
    {
        Assert.True(ScrolllerWebGate.FetchIdleTimeout > TimeSpan.Zero);
        Assert.True(ScrolllerWebGate.FetchTotalTimeout > ScrolllerWebGate.FetchIdleTimeout);
    }

    [Theory]
    [InlineData("image/jpeg", true)]
    [InlineData("image/gif", true)]
    [InlineData("IMAGE/WEBP", true)]
    [InlineData("text/html", false)]
    [InlineData("video/mp4", false)]
    [InlineData(null, false)]
    public void Only_image_bodies_are_cached_from_a_whole_fetch(string? type, bool expected)
        => Assert.Equal(expected, ScrolllerWebGate.IsCacheableType(type));

    // ---- cached answers --------------------------------------------------------------------

    private static string TempFile(int n, out byte[] data)
    {
        data = new byte[n];
        new Random(n).NextBytes(data);
        var path = Path.Combine(Path.GetTempPath(), "ccp-gate-" + Guid.NewGuid().ToString("N") + ".bin");
        File.WriteAllBytes(path, data);
        return path;
    }

    private static byte[] ReadAll(Stream s)
    {
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    [Fact]
    public void No_range_gets_the_whole_file_never_a_cut_partial()
    {
        var path = TempFile(300_000, out var data);
        try
        {
            var read = ScrolllerWebGate.OpenCached(path, null);
            Assert.NotNull(read);
            Assert.Null(read!.Value.Range);
            Assert.Equal(data.Length, read.Value.Total);
            Assert.Equal(data, ReadAll(read.Value.Body!));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_range_streams_just_that_window_and_lets_the_file_go()
    {
        var path = TempFile(300_000, out var data);
        try
        {
            var read = ScrolllerWebGate.OpenCached(path, "bytes=1000-1999");
            Assert.Equal(new ScrolllerByteRange(1000, 1999), read!.Value.Range);
            var body = read.Value.Body!;
            Assert.Equal(1000, body.Length);
            Assert.Equal(data.AsSpan(1000, 1000).ToArray(), ReadAll(body));
            // Read to its end: the gate's handle is closed, so nothing holds the file any more.
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void An_unsatisfiable_range_or_a_vanished_file()
    {
        var path = TempFile(1000, out _);
        try
        {
            var bad = ScrolllerWebGate.OpenCached(path, "bytes=5000-");
            Assert.True(bad!.Value.Unsatisfiable);
            Assert.Null(bad.Value.Body);
        }
        finally { File.Delete(path); }
        Assert.Null(ScrolllerWebGate.OpenCached(path, null));
    }

    [Fact]
    public void Own_refusals_carry_cors_and_the_marker()
    {
        var h = ScrolllerWebGate.LocalErrorHeaders();
        Assert.Contains("Access-Control-Allow-Origin: *", h);
        Assert.Contains(ScrolllerWebGate.MarkerHeader + ": local", h);
        var h416 = ScrolllerWebGate.LocalErrorHeaders("Content-Range: bytes */1000");
        Assert.StartsWith("Content-Range: bytes */1000\r\n", h416);
        Assert.Contains("Access-Control-Allow-Origin: *", h416);
    }
}
