using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.Fyp;
using ConditioningControlPanel.Services.Fyp.Online;
using ConditioningControlPanel.Services.GoonGame.Online;
using ConditioningControlPanel.Services.PieceByPiece.Media;
using Newtonsoft.Json.Linq;
using Xunit;
namespace ConditioningControlPanel.Tests;
public sealed class PbpMediaTests
{
    [Fact]
    public void ConsentChoiceChannelsAndShareMatchWpf()
    {
        foreach (bool enabled in new[] { false, true })
        foreach (bool picked in new[] { false, true })
        foreach (bool consent in new[] { false, true })
        foreach (var source in new[] { "local", "mixed", "online" })
        {
            bool allowed = enabled && (picked || consent && source != "local");
            Assert.Equal(allowed, PbpMediaRules.FetchAllowed(enabled, picked, source, consent));
            Assert.Equal(allowed ? new[] { "Own" } : Array.Empty<string>(),
                PbpMediaRules.ChannelsFor(enabled, picked, source, consent, "pink", new[] { "Own" }, new[] { "App" }));
        }
        Assert.False(PbpMediaRules.HasSavedChoice(false, true, ""));
        Assert.True(PbpMediaRules.HasSavedChoice(false, false, ""));
        Assert.True(PbpMediaRules.HasSavedChoice(false, true, "pink"));
        Assert.True(PbpMediaRules.HasSavedChoice(true, true, ""));
        Assert.True(PbpMediaRules.SavedOptIn(true, " PINK "));
        Assert.False(PbpMediaRules.SavedOptIn(false, "pink"));
        Assert.Equal(new[] { "App" }, PbpMediaRules.ChannelsFor(true, false, "mixed", true, "", null, new[] { "App" }));
        Assert.Empty(PbpMediaRules.ChannelsFor(true, true, "local", false, "", null, new[] { "App" }));
        Assert.Equal(100, PbpMediaRules.SharePct("online", 25, true));
        Assert.Equal(5, PbpMediaRules.SharePct("mixed", 0, false));
        Assert.Equal(95, PbpMediaRules.SharePct("mixed", 100, false));
        Assert.Equal(70, PbpMediaRules.SharePct("mixed", 25, true));
        foreach (var state in new[] { "off", "empty", "loading", "ready", "error" })
        {
            Assert.True(PbpMediaRules.MediaAvailable(true, null, state));
            Assert.Equal(state != "off" && state != "empty", PbpMediaRules.MediaAvailable(false, new[] { "Own" }, state));
        }
        Assert.False(PbpMediaRules.MediaAvailable(false, Array.Empty<string>()));
        Assert.Equal(new[] { "Alpha", "Beta" }, GoonOnlineMediaRules.CleanSubs(new[] { "r/Alpha", "alpha", "../bad", "Beta" }));
        Assert.Equal(8, GoonOnlineMediaRules.CleanSubs(Enumerable.Range(10, 20).Select(n => "sub" + n)).Count);
        Assert.Equal("", GoonOnlineMediaRules.CleanCustom(new JArray()));
        Assert.Equal("", GoonOnlineMediaRules.CleanCustom(new JObject { ["x"] = new string('a', 17000) }));
        Assert.Empty(GoonOnlineMediaRules.ParseCustom("not json"));
    }
    [Fact]
    public async Task WavesRefillBoundAndRelease_AndLateDownloadIsDiscardedOnClose()
    {
        var root = Directory.CreateTempSubdirectory("pbp-pool-").FullName;
        var (fetch, materialize, release, assets) = (GoonOnlineMedia.Fetch, GoonOnlineMedia.Materialize, GoonOnlineMedia.ReleaseFile, CorePaths.EffectiveAssetsProvider);
        var snapshots = new ConcurrentQueue<GoonOnlineMedia.Snapshot>();
        var released = new ConcurrentQueue<string>();
        int id = 0;
        try
        {
            CorePaths.EffectiveAssetsProvider = () => root;
            GoonOnlineMedia.Fetch = (_, _, kind, _) => Task.FromResult((Enumerable.Range(0, 24).Select(_ =>
                new FypAssetManifest.Entry { Id = "scrolller/Sub/" + Interlocked.Increment(ref id),
                    Url = "https://media.example/" + id + (kind == FeedMediaKind.GifStill ? ".jpg" : ".mp4"),
                    Type = kind == FeedMediaKind.GifStill ? "image" : "video" }).ToList(), (string?)null));
            GoonOnlineMedia.Materialize = (url, _) => Task.FromResult<string?>(Path.Combine(root, ".temp", Path.GetFileName(new Uri(url).AbsolutePath)));
            GoonOnlineMedia.ReleaseFile = released.Enqueue;
            using var pool = GoonOnlineMedia.ForGame("pbp-test", snapshots.Enqueue);
            pool.Start(new[] { "Sub" }); await pool.Pending.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("ready", snapshots.Last().State);
            Assert.Equal(24, snapshots.Last().Images.Count); Assert.Equal(12, snapshots.Last().Videos.Count);
            int calls = id;
            pool.Start(new[] { "sub" }); Assert.Equal(calls, id); // same deck, no refetch
            for (int wave = 0; wave < 3; wave++) { Assert.True(pool.More()); await pool.Pending.WaitAsync(TimeSpan.FromSeconds(10)); }
            Assert.Equal(72, snapshots.Last().Images.Count); Assert.Equal(36, snapshots.Last().Videos.Count);
            Assert.Equal(108, snapshots.Last().Images.Concat(snapshots.Last().Videos).Select(i => i.Url).Distinct().Count());
            pool.Off(); Assert.Equal("off", snapshots.Last().State); Assert.False(pool.More());
            Assert.True(released.Count >= 108);
            var began = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var late = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            GoonOnlineMedia.Materialize = (_, _) => { began.TrySetResult(); return late.Task; };
            pool.Start(new[] { "Other" }); await began.Task.WaitAsync(TimeSpan.FromSeconds(10));
            pool.Dispose(); late.SetResult(Path.Combine(root, ".temp", "late.jpg"));
            await pool.Pending.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Contains(released, p => p.EndsWith("late.jpg", StringComparison.Ordinal));
            Assert.False(pool.More());
        }
        finally
        {
            (GoonOnlineMedia.Fetch, GoonOnlineMedia.Materialize, GoonOnlineMedia.ReleaseFile, CorePaths.EffectiveAssetsProvider) = (fetch, materialize, release, assets);
            Directory.Delete(root, true);
        }
    }
    [Fact]
    public async Task HeadSpecificFileOwnersStayWithTheirPool()
    {
        var root = Directory.CreateTempSubdirectory("pbp-owners-").FullName;
        var oldFetch = GoonOnlineMedia.Fetch;
        var oldAssets = CorePaths.EffectiveAssetsProvider;
        var first = new ConcurrentQueue<string>();
        var second = new ConcurrentQueue<string>();
        int id = 0;
        try
        {
            CorePaths.EffectiveAssetsProvider = () => root;
            GoonOnlineMedia.Fetch = (_, _, kind, _) => Task.FromResult((new List<FypAssetManifest.Entry>
            {
                new() { Id = "scrolller/Sub/" + Interlocked.Increment(ref id),
                    Url = "https://media.example/picture.jpg", Type = "image" }
            }, (string?)null));
            using var a = GoonOnlineMedia.ForGame("pbp-owner-a", _ => { },
                (_, _) => Task.FromResult<string?>(Path.Combine(root, "first.jpg")), first.Enqueue);
            using var b = GoonOnlineMedia.ForGame("pbp-owner-b", _ => { },
                (_, _) => Task.FromResult<string?>(Path.Combine(root, "second.jpg")), second.Enqueue);
            a.Start(new[] { "Sub" }); b.Start(new[] { "Other" });
            await Task.WhenAll(a.Pending, b.Pending).WaitAsync(TimeSpan.FromSeconds(10));
            a.Off();
            Assert.NotEmpty(first);
            Assert.All(first, path => Assert.EndsWith("first.jpg", path));
            Assert.Empty(second);
            b.Dispose();
            Assert.NotEmpty(second);
            Assert.All(second, path => Assert.EndsWith("second.jpg", path));
        }
        finally
        {
            GoonOnlineMedia.Fetch = oldFetch;
            CorePaths.EffectiveAssetsProvider = oldAssets;
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData(false, "empty")]
    [InlineData(true, "error")]
    public async Task DryOrFailedFeedStopsAfterThreeBatches(bool failed, string state)
    {
        var old = GoonOnlineMedia.Fetch;
        var oldDelay = GoonOnlineMedia.RetryDelay;
        var snaps = new ConcurrentQueue<GoonOnlineMedia.Snapshot>();
        int calls = 0, delays = 0;
        try
        {
            GoonOnlineMedia.RetryDelay = _ => { Interlocked.Increment(ref delays); return Task.CompletedTask; };
            GoonOnlineMedia.Fetch = (_, _, _, _) =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult((new List<FypAssetManifest.Entry>(), failed ? "offline" : null));
            };
            using var pool = GoonOnlineMedia.ForGame("pbp-dry", snaps.Enqueue);
            pool.Start(new[] { "Sub" }); await pool.Pending.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(state, snaps.Last().State); Assert.Equal(0, snaps.Last().Have);
            Assert.Equal(6, calls); // three dry batches per kind
            Assert.Equal(failed ? 6 : 0, delays); // a failed batch backs off, a dry one does not
            pool.Start(Array.Empty<string>()); Assert.Equal("empty", snaps.Last().State);
            Assert.False(pool.More());
        }
        finally { GoonOnlineMedia.Fetch = old; GoonOnlineMedia.RetryDelay = oldDelay; }
    }

}
