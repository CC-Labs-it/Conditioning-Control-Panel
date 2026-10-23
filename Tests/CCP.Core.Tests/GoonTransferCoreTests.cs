using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.Transfer;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The Goon Game transfer stack after its move from the WPF head: the seams a head
/// seeds (video engine, library walk, Discord link, UI-thread post) and what each does unseeded.
/// Fail-proven: PreviewWindow without the clamp turns ThePreviewWindow red at 2100 ms; a
/// ReferenceEquals re-attach check turns ReAttachingTheSamePage red.</summary>
[Collection(SessionStatics.Name)] // CoreDispatch and the lane seams are process-global
public sealed class GoonTransferCoreTests : IDisposable
{
    private readonly Action<Action>? _post = CoreDispatch.PostProvider;
    private readonly Func<string, CancellationToken, Task<VideoTranscodeLane.VideoProbe?>>? _probe = VideoTranscodeLane.Probe;
    private readonly Func<IEnumerable<(string, string, long, bool)>>? _pool = AssetCompressionPlanner.ActivePool;
    private readonly List<string> _tmp = new();

    public void Dispose()
    {
        GoonCacheBridge.Detach();
        CoreDispatch.PostProvider = _post;
        VideoTranscodeLane.Probe = _probe;
        AssetCompressionPlanner.ActivePool = _pool;
        foreach (var f in _tmp) try { File.Delete(f); } catch (IOException) { } // swallow: temp cleanup
    }

    [Theory]
    [InlineData(10_000, 1_200, 3_200)]   // 12% in, two seconds long
    [InlineData(2_100, 100, 2_100)]      // 12% would overrun the end: clamped back
    [InlineData(1_500, 0, 1_500)]        // shorter than the window: the whole clip
    [InlineData(0, 0, 2_000)]            // unknown duration: the first two seconds
    public void ThePreviewWindow(int durMs, double start, double stop)
        => Assert.Equal((start, stop), VideoTranscodeLane.PreviewWindow(durMs));

    [Fact]
    public async Task WithNoVideoEngine_AProbeIsUnknown_AndATranscodeIsNoDecoder()
    {
        VideoTranscodeLane.Probe = null;
        Assert.Null(await VideoTranscodeLane.ProbeAsync("clip.mp4"));
        var ex = await Assert.ThrowsAsync<TranscodeUnsupportedException>(
            () => VideoTranscodeLane.TranscodeAsync("clip.mp4", "out.mp4", null));
        Assert.Equal(TransferFailReasons.NoDecoder, ex.Reason);

        VideoTranscodeLane.Probe = (_, _) => throw new IOException("shell shrugged");
        Assert.Null(await VideoTranscodeLane.ProbeAsync("clip.mp4"));   // a throwing engine is "unknown" too
    }

    [Fact]
    public async Task ThePlannerWalksOnlyTheHeadsPool()
    {
        var store = new TransferCacheStore();
        AssetCompressionPlanner.ActivePool = null;
        Assert.Equal(0, (await AssetCompressionPlanner.PlanAsync(store)).PoolCount);

        var small = Path.Combine(Path.GetTempPath(), "goon-plan-" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(small, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        _tmp.Add(small);
        AssetCompressionPlanner.ActivePool = () => new[] { (small, "images/a.png", 4L, true) };
        var plan = await AssetCompressionPlanner.PlanAsync(store);
        Assert.Equal(1, plan.PoolCount);
        Assert.Equal(TransferLanes.Exempt, Assert.Single(plan.Jobs).Lane);   // 5 MB or less ships as it is
    }

    [Fact]
    public async Task AnUnlinkedHeadNeverFetchesAnAvatar()
    {
        var old = GoonAvatarCache.OwnDiscordProvider;
        try
        {
            GoonAvatarCache.OwnDiscordProvider = null;
            Assert.Null(await GoonAvatarCache.RefreshOwnAvatarAsync());
            GoonAvatarCache.OwnDiscordProvider = () => (true, null, null);   // linked, default avatar
            Assert.Null(await GoonAvatarCache.RefreshOwnAvatarAsync());
        }
        finally { GoonAvatarCache.OwnDiscordProvider = old; }
    }

    [Fact]
    public void ReAttachingTheSamePage_IsANoOp_AndFramesGoThroughCoreDispatch()
    {
        int hops = 0;
        CoreDispatch.PostProvider = a => { hops++; a(); };
        var page = new FakePage();

        GoonCacheBridge.Attach(page.Post);
        Assert.True(hops > 0);
        var first = page.Frames.FindAll(f => (string?)f["type"] == "cache-state").Count;
        Assert.Equal(1, first);

        GoonCacheBridge.Attach(page.Post);   // a new delegate, same page: equal, so nothing resets
        Assert.Equal(first, page.Frames.FindAll(f => (string?)f["type"] == "cache-state").Count);

        GoonCacheBridge.Detach();
        GoonCacheBridge.OnMessage(new JObject { ["type"] = "cache-req", ["op"] = "list" });
        Assert.DoesNotContain(page.Frames, f => (string?)f["type"] == "cache-list");
    }

    private sealed class FakePage
    {
        public readonly List<JObject> Frames = new();
        public void Post(object msg) { lock (Frames) Frames.Add(JObject.FromObject(msg)); }
    }
}
