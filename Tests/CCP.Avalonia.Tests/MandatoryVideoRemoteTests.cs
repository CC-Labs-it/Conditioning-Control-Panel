using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class MandatoryVideoRemoteTests
{
    [Fact]
    public void Playback_rechecks_the_cache_and_never_streams_an_evicted_scrolller_clip()
    {
        Assert.Null(MandatoryVideoOverlay.PlaybackPath("https://v.scrolller.com/never-cached-test.mp4"));
        Assert.Equal("/cache.mp4", MandatoryVideoOverlay.PlaybackPath("https://v.scrolller.com/cached.mp4", _ => "/cache.mp4"));
        Assert.Equal("/local.mp4", MandatoryVideoOverlay.PlaybackPath("/local.mp4"));
        Assert.Equal("https://example.com/clip.mp4", MandatoryVideoOverlay.PlaybackPath("https://example.com/clip.mp4"));
        Assert.True(MandatoryVideoOverlay.Instance.Scheduler.RemoteMedia);
    }
    [Fact]
    public void Scrolller_clip_has_a_credit_mark_but_local_video_does_not() => AvaloniaTestDispatcher.Run(() =>
    {
        Assert.True(MandatoryVideoOverlay.CreditFor("https://v.scrolller.com/clip.mp4").Shown);
        const string cdn = "https://example.com/credited-test.mp4";
        Assert.True(MandatoryVideoOverlay.CreditFor(cdn, _ => new ScrolllerCacheMeta("clip", "test")).Shown);
        Assert.False(MandatoryVideoOverlay.CreditFor("/local.mp4").Shown);
    });
}
