using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// A clip a WebView2 page streams past the gate is charged by its length, but Chromium's own cache
/// can answer a replay without the network and the gate cannot tell: in one day a url is never
/// charged past its whole size, so a free user does not run out on replays.
/// </summary>
public class ScrolllerGateChargeTests
{
    [Theory]
    [InlineData(0, 1000, 5000L, 1000)]      // first slice
    [InlineData(1000, 4000, 5000L, 4000)]   // the rest of the file
    [InlineData(5000, 5000, 5000L, 0)]      // a cached replay pays nothing
    [InlineData(4500, 1000, 5000L, 500)]    // capped at the whole size
    [InlineData(0, 800, null, 800)]         // no known size: the first response pays
    [InlineData(800, 800, null, 0)]         // and only the first
    [InlineData(0, 0, 5000L, 0)]
    public void A_url_never_pays_past_its_size_in_a_day(long already, long bytes, long? total, long expected)
        => Assert.Equal(expected, ScrolllerWebGate.DedupedCharge(already, bytes, total));

    [Theory]
    [InlineData(206, null, "bytes 0-1023/52000", 52000L)]
    [InlineData(206, "1024", "bytes 0-1023/*", null)]
    [InlineData(200, "52000", null, 52000L)]
    [InlineData(200, null, null, null)]
    [InlineData(200, "abc", null, null)]
    public void Reads_the_whole_size(int status, string? length, string? range, long? expected)
        => Assert.Equal(expected, ScrolllerWebGate.TotalSize(status, length, range));
}
