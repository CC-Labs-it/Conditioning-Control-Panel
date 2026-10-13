using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Transfer;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>TransferCacheStore, moved from the WPF head: the LRU policy, the cap and preset
/// protection read through CoreSettings, and the index round trip under the sandbox profile
/// (RoadmapTestProfile points CCP_USERDATA_DIR, so CorePaths.UserData, at a temp dir).
/// Fail-proven: the LRU pass ordered OrderByDescending(LastUsed) turns PlanEvictions_StaleFailuresFirst red;
/// CapBytes reading DefaultCapBytes instead of CoreSettings turns CapBytes_ReadsCoreSettings_Clamped red.</summary>
[Collection(SessionStatics.Name)] // CoreSettings.ServiceProvider is process-global
public sealed class TransferCacheStoreTests : IDisposable
{
    private const long MB = 1024 * 1024;
    private readonly Func<SettingsService?>? _oldProvider = CoreSettings.ServiceProvider;
    private readonly SettingsService _settings = new();

    public TransferCacheStoreTests()
    {
        CoreSettings.ServiceProvider = () => _settings;
        WipeCache();
    }

    public void Dispose()
    {
        CoreSettings.ServiceProvider = _oldProvider;
        WipeCache();
    }

    private static void WipeCache()
    {
        var probe = new TransferCacheStore();
        foreach (var d in new[] { probe.Root, probe.TmpDir })
            if (Directory.Exists(d)) Directory.Delete(d, recursive: true);
    }

    private static TransferCacheStore.EvictCandidate Cand(string key, long bytes, int ageDays,
        bool failed = false, bool pinned = false, bool busy = false, bool prot = false, int madeDaysAgo = 0) =>
        new(key, bytes, DateTime.UtcNow.AddDays(-ageDays), DateTime.UtcNow.AddDays(-madeDaysAgo),
            failed, pinned, busy, prot);

    /// <summary>Ready entry with a real artifact on disk, so Reindex keeps it.</summary>
    private static TransferCacheEntry Ready(TransferCacheStore store, string rel, string sha, long bytes, int ageDays = 0)
    {
        store.EnsureRoot();
        File.WriteAllBytes(store.ArtifactPathForSha(sha, "webp"), new byte[] { 1 });
        return new TransferCacheEntry
        {
            Rel = rel, State = TransferStates.Ready, Sha = sha, Ext = "webp", Bytes = bytes,
            LastUsed = DateTime.UtcNow.AddDays(-ageDays),
        };
    }

    [Fact]
    public void PlanEvictions_StaleFailuresFirst_ThenColdestUnprotected_NeverPinnedOrBusy()
    {
        var cands = new[]
        {
            Cand("stale-fail", 0, 30, failed: true, madeDaysAgo: 20),
            Cand("fresh-fail", 0, 0, failed: true, madeDaysAgo: 2),
            Cand("pinned", 400 * MB, 99, pinned: true),
            Cand("busy", 400 * MB, 98, busy: true),
            Cand("cold", 100 * MB, 50),
            Cand("warm", 100 * MB, 10),
            Cand("hot", 100 * MB, 1),
            Cand("protected-cold", 100 * MB, 60, prot: true),
        };

        var drop = TransferCacheStore.PlanEvictions(cands, 1200 * MB, 1050 * MB, DateTime.UtcNow, out var over);

        Assert.Equal(new[] { "stale-fail", "cold", "warm" }, drop);
        Assert.False(over);
    }

    [Fact]
    public void PlanEvictions_ProtectedOnlyWhenUnprotectedIsNotEnough_AndSaysSo()
    {
        var cands = new[] { Cand("free", 100 * MB, 1), Cand("prot-hot", 100 * MB, 1, prot: true), Cand("prot-cold", 100 * MB, 9, prot: true) };

        var drop = TransferCacheStore.PlanEvictions(cands, 300 * MB, 150 * MB, DateTime.UtcNow, out var over);

        Assert.Equal(new[] { "free", "prot-cold" }, drop);
        Assert.True(over);
    }

    [Theory]
    [InlineData(0L, TransferCacheStore.DefaultCapBytes)]
    [InlineData(1L, TransferCacheStore.MinCapBytes)]
    [InlineData(long.MaxValue, TransferCacheStore.MaxCapBytes)]
    [InlineData(3L * 1024 * MB, 3L * 1024 * MB)]
    public void CapBytes_ReadsCoreSettings_Clamped(long setting, long expected)
    {
        _settings.Current.TransferCacheCapBytes = setting;
        Assert.Equal(expected, new TransferCacheStore().CapBytes);
    }

    [Fact]
    public void Upsert_RetiresTheStaleEntryForTheSameRel_AndTheIndexSurvivesAReload()
    {
        var store = new TransferCacheStore();
        store.Load();
        store.Upsert("k-old", Ready(store, "images/a.png", "aaaa", 10));
        store.Upsert("k-new", Ready(store, "images/a.png", "bbbb", 20));
        store.Save();

        Assert.False(store.TryGet("k-old", out _));
        Assert.False(File.Exists(store.ArtifactPathForSha("aaaa", "webp")));

        var reloaded = new TransferCacheStore();
        reloaded.Load();
        Assert.True(reloaded.TryGetBySha("bbbb", out var e));
        Assert.Equal("images/a.png", e.Rel);
        Assert.Equal(20, reloaded.UsedBytes);
    }

    [Fact]
    public void Load_DropsAReadyEntryWhoseArtifactVanished()
    {
        var store = new TransferCacheStore();
        store.Load();
        store.Upsert("k", Ready(store, "images/a.png", "cccc", 10));
        store.Save();
        File.Delete(store.ArtifactPathForSha("cccc", "webp"));

        var reloaded = new TransferCacheStore();
        reloaded.Load();
        Assert.Equal(0, reloaded.Counts().Total);
        reloaded.Save();
    }

    [Fact]
    public void EnforceCap_SparesRelsEnabledInARecentPreset_ReadThroughCoreSettings()
    {
        _settings.Current.TransferCacheCapBytes = 1;   // clamps to the 1 GB floor
        _settings.Current.AssetPresets = new List<AssetPreset>
        {
            new() { LastUsed = DateTime.UtcNow, DisabledAssetPaths = new HashSet<string> { "images/off.png" } },
        };
        var store = new TransferCacheStore();
        store.Load();
        store.Upsert("on", Ready(store, "images/on.png", "dddd", 700 * MB, ageDays: 9));    // colder, but protected
        store.Upsert("off", Ready(store, "images/off.png", "eeee", 700 * MB, ageDays: 1));

        store.EnforceCap();
        store.Save();

        Assert.True(store.TryGet("on", out _));
        Assert.False(store.TryGet("off", out _));
        Assert.False(store.OverCapProtected);
    }
}
