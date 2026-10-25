using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Services.Fyp;
using Xunit;
using E = ConditioningControlPanel.Services.Chaos.DtrhRemoteMedia.RemoteEntry;

namespace ConditioningControlPanel.Tests;

/// <summary>The DtRH manifest's online half (WPF DtrhAssetManifest remote media, now Core DtrhRemoteMedia):
/// the gates, the names hostMedia.js parses, the copy order, the prune, the refill pick with the loops-dry
/// back-off (f96e84ed4), and copies landing as asset urls with the library stepping aside (724ac82bc).</summary>
[Collection(CCP.Core.Tests.SessionStatics.Name)]   // CoreSettings provider + the process-wide remote cache
public class DtrhRemoteMediaTests
{
    private static E Still(int i, long at = 1000) => new($"scrolller/Sub/{i}", $"https://cdn/{i}.webp", true, at);
    private static E Loop(int i, long at = 1000) => new($"scrolller/Sub/l{i}", $"https://cdn/l{i}.mp4", false, at, IsLoop: true);
    private static E Clip(int i, long at = 1000) => new($"scrolller/Sub/c{i}", $"https://cdn/c{i}.webm", false, at);

    [Fact]
    public void CountKinds_LoopsAndClipsAreNotStills()   // was WPF DtrhRemoteKindsTests
    {
        var cache = Enumerable.Range(0, 30).Select(i => Clip(i)).ToList();
        Assert.Equal((0, 30), DtrhRemoteMedia.CountKinds(cache));
        cache.Add(Still(1));
        Assert.Equal((1, 30), DtrhRemoteMedia.CountKinds(cache));
        Assert.Equal((0, 0), DtrhRemoteMedia.CountKinds(null));
    }

    [Fact]
    public void Share_NeedsAnOnlineSourceAndConsent_MixedIsClamped()
    {
        Assert.Null(DtrhRemoteMedia.Share(null));
        var s = new AppSettings { MediaSource = "online" };
        Assert.Null(DtrhRemoteMedia.Share(s));                 // no consent card accepted
        s.RemoteMediaConsented = true;
        Assert.Equal(100, DtrhRemoteMedia.Share(s));
        s.MediaSource = "local";
        Assert.Null(DtrhRemoteMedia.Share(s));
        s.MediaSource = "mixed";
        s.RemoteMediaRatio = 99;
        Assert.Equal(95, DtrhRemoteMedia.Share(s));
        s.RemoteMediaRatio = 0;
        Assert.Equal(5, DtrhRemoteMedia.Share(s));
    }

    [Fact]
    public void EntryName_CarriesTheShareAndTheLoopMarker()
    {
        Assert.Equal("online30:Sub-1.webp", DtrhRemoteMedia.EntryName(30, Still(1)));
        Assert.Equal("online100:loop:Sub-l2.mp4", DtrhRemoteMedia.EntryName(100, Loop(2)));
        Assert.Equal("a-b.webm", DtrhRemoteMedia.NameFor(new E("a/b", "https://x/y.webm?sig=1", false, 0)));
    }

    [Fact]
    public void CopyOrder_LoopsThenStillsThenAtMost24Clips_SkippingCopied()
    {
        var pool = new List<E>();
        pool.AddRange(Enumerable.Range(0, 30).Select(i => Clip(i)));
        pool.Add(Still(1));
        pool.Add(Loop(1));
        pool.Add(Still(2));
        var order = DtrhRemoteMedia.CopyOrder(pool, url => url == "https://cdn/2.webp" || url == "https://cdn/c0.webm");
        Assert.Equal(Loop(1), order[0]);
        Assert.Equal(Still(1), order[1]);
        Assert.Equal(Clip(1), order[2]);
        Assert.Equal(25, order.Count);                         // 1 loop + 1 still + 23 of the first 24 clips
        Assert.DoesNotContain(Clip(24), order);
    }

    [Fact]
    public void Prune_DropsPastTheTtl_ThenTheOldestOver30PerKind()
    {
        long now = 10_000_000;
        var cache = new List<E> { Still(0, now - DtrhRemoteMedia.RemoteEntryTtlSeconds - 1) };
        cache.AddRange(Enumerable.Range(1, 32).Select(i => Clip(i, now - 100 + i)));
        cache.Add(Still(1, now));
        DtrhRemoteMedia.Prune(cache, now);
        Assert.Equal(31, cache.Count);
        Assert.Contains(Still(1, now), cache);
        Assert.DoesNotContain(cache, e => e.Id.EndsWith("/c1") || e.Id.EndsWith("/c2"));   // oldest clips went
    }

    [Fact]
    public void PickRefill_NeediestFirst_TieGoesLoops_ADryLoopsKindSitsOut()
    {
        var empty = new List<E>();
        Assert.Equal(DtrhRemoteMedia.Kind.Loops, DtrhRemoteMedia.PickRefill(empty, loopsDry: false));
        Assert.Equal(DtrhRemoteMedia.Kind.Stills, DtrhRemoteMedia.PickRefill(empty, loopsDry: true));
        var full = Enumerable.Range(0, 12).SelectMany(i => new[] { Still(i), Clip(i) }).ToList();
        Assert.Equal(DtrhRemoteMedia.Kind.Loops, DtrhRemoteMedia.PickRefill(full, loopsDry: false));
        Assert.Null(DtrhRemoteMedia.PickRefill(full, loopsDry: true));                  // dry loops do not keep it fetching
        var noClips = Enumerable.Range(0, 12).SelectMany(i => new[] { Still(i), Loop(i) }).ToList();
        Assert.Equal(DtrhRemoteMedia.Kind.Clips, DtrhRemoteMedia.PickRefill(noClips, loopsDry: false));
    }

    [Fact]
    public void Cache_RoundTripsEveryField()
    {
        var cache = new[] { Still(1, 5), Loop(2, 6), Clip(3, 7) };
        Assert.Equal(cache, DtrhRemoteMedia.ParseCache(DtrhRemoteMedia.SerializeCache(cache)));
        Assert.Empty(DtrhRemoteMedia.ParseCache("{\"entries\":[{\"id\":\"\",\"url\":\"u\"}]}"));
    }

    [Fact]
    public async Task SaveCapped_RefusesPastTheCap()
    {
        var dir = Directory.CreateTempSubdirectory("dtrh-copy-").FullName;
        try
        {
            var path = Path.Combine(dir, "a.bin");
            Assert.True(await DtrhRemoteMedia.SaveCappedAsync(new MemoryStream(new byte[10]), path, CancellationToken.None, cap: 10));
            Assert.False(await DtrhRemoteMedia.SaveCappedAsync(new MemoryStream(new byte[11]), path, CancellationToken.None, cap: 10));
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>The whole pass: a cold cache shows nothing and fetches loops; a dry loops answer backs loops off for
    /// 30 minutes (the next fetch is stills); stills land, get copies, raise CopiesReady, and the next manifest
    /// hands them out as asset urls - with "online" and 8 copies the library steps aside.</summary>
    [Fact]
    public async Task Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls()
    {
        var restore = (CoreSettings.ServiceProvider, DtrhRemoteMedia.NowUnix, DtrhRemoteMedia.Fetch);
        var settings = new SettingsService();
        settings.Current.MediaSource = "online";
        settings.Current.RemoteMediaConsented = true;
        var cachePath = Path.Combine(CorePaths.UserData, "dtrh_remote_media.json");
        long now = 2_000_000_000;
        var asked = new List<DtrhRemoteMedia.Kind>();
        int ready = 0;
        void OnReady() => Interlocked.Increment(ref ready);
        var copies = Directory.CreateTempSubdirectory("dtrh-copies-").FullName;
        var copier = new DtrhRemoteMedia.Copier((url, _) =>
        {
            var path = Path.Combine(copies, url[(url.LastIndexOf('/') + 1)..]);
            File.WriteAllBytes(path, new byte[] { 1 });
            return Task.FromResult<string?>(path);
        }, _ => { });
        try
        {
            Directory.CreateDirectory(CorePaths.UserData);
            File.Delete(cachePath);
            DtrhRemoteMedia.ResetForTests();
            CoreSettings.ServiceProvider = () => settings;
            DtrhRemoteMedia.NowUnix = () => now;
            DtrhRemoteMedia.Fetch = (kind, _) =>
            {
                asked.Add(kind);
                var list = kind == DtrhRemoteMedia.Kind.Stills
                    ? Enumerable.Range(0, 9).Select(i => new FypAssetManifest.Entry { Id = $"scrolller/Sub/{i}", Url = $"https://cdn/{i}.webp", Type = "image", Origin = "online" }).ToList()
                    : new List<FypAssetManifest.Entry>();
                return Task.FromResult((list, (string?)null));
            };
            DtrhRemoteMedia.CopiesReady += OnReady;

            var first = DtrhRemoteMedia.Take(settings.Current, c => "asset:" + c, copier);
            Assert.Empty(first!.Value.Entries);                                         // one launch behind
            await DtrhRemoteMedia.RefillTask;
            Assert.Equal(new[] { DtrhRemoteMedia.Kind.Loops }, asked);

            now += 60;                                                                  // inside the 30 min back-off
            DtrhRemoteMedia.Take(settings.Current, c => "asset:" + c, copier);
            await DtrhRemoteMedia.RefillTask;
            await DtrhRemoteMedia.CopyTask;
            Assert.Equal(DtrhRemoteMedia.Kind.Stills, asked[1]);
            Assert.True(ready > 0);

            var m = new GameMediaManifest.Manifest();
            m.Images.Add(new GameMediaManifest.Entry("lib.png", "/ccp.assets/images/lib.png"));
            Assert.Equal(9, DtrhRemoteMedia.AppendTo(m, settings.Current, c => "asset:" + c, copier));
            Assert.DoesNotContain(m.Images, e => e.Name == "lib.png");                 // 9 copies >= 8: online is online only
            Assert.Contains(new GameMediaManifest.Entry("online100:Sub-0.webp", "asset:" + Path.Combine(copies, "0.webp")), m.Images);
            await DtrhRemoteMedia.RefillTask;   // AppendTo kicked a refill; its save truncates the file mid-read
            Assert.Contains("\"id\":\"scrolller/Sub/0\"", File.ReadAllText(cachePath));

            // AppendTo starts a clips refill while loops are still backed off. Let it finish before
            // moving the clock, or the next Take may only observe that older in-flight request.
            await DtrhRemoteMedia.RefillTask;
            Assert.Equal(DtrhRemoteMedia.Kind.Clips, asked[^1]);

            now += DtrhRemoteMedia.LoopsDrySeconds;                                     // the back-off is over
            DtrhRemoteMedia.Take(settings.Current, c => "asset:" + c, copier);
            await DtrhRemoteMedia.RefillTask;
            Assert.Equal(DtrhRemoteMedia.Kind.Loops, asked[^1]);
        }
        finally
        {
            DtrhRemoteMedia.CopiesReady -= OnReady;
            (CoreSettings.ServiceProvider, DtrhRemoteMedia.NowUnix, DtrhRemoteMedia.Fetch) = restore;
            DtrhRemoteMedia.ResetForTests();
            File.Delete(cachePath);
            Directory.Delete(copies, true);
        }
    }
}
