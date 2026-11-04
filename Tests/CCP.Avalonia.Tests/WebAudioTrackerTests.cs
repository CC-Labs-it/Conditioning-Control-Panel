using System;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Audio;
using Xunit;

namespace CCP.Avalonia.Tests;

public class WebAudioTrackerTests
{
    private sealed class Page
    {
        public event Action? AudioChanged;
        public bool Playing, Muted;
        public string? Source;
        public void Raise() => AudioChanged?.Invoke();
    }

    [Theory]
    [InlineData(false, false, "web:ccp.race3d")]
    [InlineData(false, true, "web:ccp.race3d:muted")]
    [InlineData(true, false, "web:ccp.race3d")]
    [InlineData(true, true, "web:ccp.race3d:muted")]
    public void Audio_start_reaches_ring_once_after_repeated_attachment(bool running, bool muted, string player)
    {
        var ring = new RecentSounds();
        var tracker = new WebAudioTracker(ring, () => running);
        var page = new Page { Source = "https://ccp.race3d/index.html?x=1", Muted = muted };
        void Attach() => tracker.Attach(page, callback => page.AudioChanged += callback,
            () => (page.Playing, page.Source, page.Muted));
        Attach();
        Attach();
        page.Playing = true;
        page.Raise();
        var entry = Assert.Single(ring.Snapshot());
        Assert.Equal(player, entry.Player);
        Assert.Equal("index.html", entry.File);
        Assert.Equal(running, entry.EngineRunning);
        Assert.False(entry.Asked);
        Assert.Equal(1, entry.Count);
        page.Playing = false;
        page.Raise();
        entry = Assert.Single(ring.Snapshot());
        Assert.Equal(1, entry.Count);
    }

    [Fact]
    public void Stop_without_a_start_does_not_reach_ring()
    {
        var ring = new RecentSounds();
        var tracker = new WebAudioTracker(ring, () => false);
        var page = new Page();
        tracker.Attach(page, callback => page.AudioChanged += callback,
            () => (page.Playing, page.Source, page.Muted));
        page.Raise();
        Assert.Empty(ring.Snapshot());
    }
}
