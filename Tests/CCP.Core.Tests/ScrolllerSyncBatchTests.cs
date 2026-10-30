using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace CCP.Core.Tests;

public sealed class ScrolllerSyncBatchTests
{
    [Theory]
    [InlineData(86399, true)]
    [InlineData(86400, false)]
    [InlineData(172800, false)]
    public void Dtrh_entries_expire_at_one_day(long age, bool expected)
    {
        const long now = 1790000000;
        var entry = new ConditioningControlPanel.Services.Chaos.DtrhRemoteMedia.RemoteEntry(
            "scrolller/sub/1", "https://images.scrolller.com/a.jpg", true, now - age);
        Assert.Equal(expected, ConditioningControlPanel.Services.Chaos.DtrhRemoteMedia.RemoteEntryFresh(entry, now));
    }

    [Fact]
    public void Bug_report_scrubs_scrolller_content_ids_and_urls()
    {
        var (text, counts) = LogScrubber.Scrub("scrolller/sub/post123 https://images.scrolller.com/private.jpg");
        Assert.DoesNotContain("post123", text);
        Assert.DoesNotContain("private.jpg", text);
        Assert.Equal(2, counts.Scrolller);
    }

    [Theory]
    [InlineData("https://images.scrolller.com/private/post.gif?token=secret", "images.scrolller.com")]
    [InlineData("/private/scrolller/sub/post.gif", "file .gif")]
    [InlineData("file:///private/post.gif", "file .gif")]
    [InlineData(null, "<none>")]
    public void Corner_gif_logs_only_host_or_extension(string? source, string expected)
        => Assert.Equal(expected, CornerGifPlanner.LogWhere(source));

    [Theory]
    [InlineData("scrolller", true, true)]
    [InlineData("mixed", true, true)]
    [InlineData("local", true, false)]
    [InlineData("scrolller", false, false)]
    [InlineData(null, true, false)]
    public void Wallpaper_credit_tracks_online_admission(string? source, bool consent, bool expected)
        => Assert.Equal(expected, ScrolllerCreditRules.WallpaperMediaEnabled(source, consent));
}
