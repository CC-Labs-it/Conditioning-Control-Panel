using ConditioningControlPanel.Services.Chaos;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// The DtRH telemetry stores, now in Core (CorePaths.UserData is this assembly's temp sandbox).
/// Both stores are process-wide statics, so each test compares deltas / uses its own asset names.
/// </summary>
public class DtrhStatsStoresTests
{
    [Fact]
    public void AssetStats_GrabsOutrankRawAttention()
    {
        DtrhAssetStatsStore.Merge(JObject.Parse("""
            {"type":"asset-stats","stats":[
              {"name":"zz_watched.png","kind":"image","weighted":20},
              {"name":"zz_grabbed.png","kind":"image","weighted":1,"grabs":3}
            ]}
            """));

        var top = DtrhAssetStatsStore.TopAssets(1000);
        Assert.Contains("zz_grabbed.png", top);
        Assert.Contains("zz_watched.png", top);
        Assert.True(top.IndexOf("zz_grabbed.png") < top.IndexOf("zz_watched.png"),
            "3 grabs (24) must outrank 20 weighted seconds");
    }

    [Fact]
    public void AssetStats_NegativeDeltasNeverSubtract()
    {
        DtrhAssetStatsStore.Merge(JObject.Parse("""{"stats":[{"name":"zz_neg_a.png","weighted":5}]}"""));
        DtrhAssetStatsStore.Merge(JObject.Parse("""{"stats":[{"name":"zz_neg_b.png","weighted":4}]}"""));
        DtrhAssetStatsStore.Merge(JObject.Parse("""{"stats":[{"name":"zz_neg_a.png","weighted":-100}]}"""));

        var top = DtrhAssetStatsStore.TopAssets(1000);
        Assert.Contains("zz_neg_a.png", top);
        Assert.Contains("zz_neg_b.png", top);
        Assert.True(top.IndexOf("zz_neg_a.png") < top.IndexOf("zz_neg_b.png"));
    }

    [Fact]
    public void SessionStats_SumsTotalsButMaxesBestCombo()
    {
        var before = DtrhSessionStatsStore.Record(JObject.Parse("""{"bestCombo":0}"""), "Gentle");
        long runs = before.Runs, popped = before.BubblesPopped, best = before.BestComboEver;

        DtrhSessionStatsStore.Record(JObject.Parse("""{"bubblesPopped":7,"bestCombo":900000}"""), "Gentle");
        var after = DtrhSessionStatsStore.Record(JObject.Parse("""{"bubblesPopped":5,"bestCombo":3}"""), "");

        Assert.Equal(runs + 2, after.Runs);
        Assert.Equal(popped + 12, after.BubblesPopped);
        Assert.Equal(System.Math.Max(best, 900000), after.BestComboEver);
    }
}
